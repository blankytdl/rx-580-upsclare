// SPDX-License-Identifier: GPL-2.0-or-later
//
// FSR4 GFX803 DXIL pipeline bridge for ReShade.
// Based on the Windows shader-replacement approach from:
// https://github.com/zgauthier2000/fsr4-shader-performance-overrides
// Original add-on copyright (C) 2026 the fsr4-shader-performance-overrides contributors.
// Changes for this repository: per-install configuration, FSR4-only shader dumping,
// bounded DXIL validation, verbose diagnostics, and a configurable override directory.
//
// This add-on does not inject into a game by itself. ReShade must be installed with full
// add-on support before the game starts. It intercepts D3D12 pipeline creation and replaces
// matching FSR4 compute shaders by the 16-byte DXBC container hash.

#include <reshade.hpp>

#include <Windows.h>
#include <algorithm>
#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cctype>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

using namespace reshade::api;

namespace
{
    constexpr size_t kMinDxilContainerSize = 32;
    constexpr size_t kMaxShaderBytes = 32u * 1024u * 1024u;

    std::filesystem::path g_addon_directory;
    std::filesystem::path g_override_directory;
    std::filesystem::path g_dump_directory;
    bool g_enabled = true;
    bool g_dump_shaders = false;
    bool g_verbose = false;
    bool g_only_fsr4 = true;

    std::mutex g_mutex;
    std::unordered_map<std::string, std::vector<uint8_t>> g_replacement_cache;
    std::atomic<uint64_t> g_fsr4_seen{0};
    std::atomic<uint64_t> g_replaced{0};
    std::atomic<uint64_t> g_missing{0};
    std::atomic<uint64_t> g_invalid{0};

    void Log(reshade::log::level level, const std::string &message)
    {
        reshade::log::message(level, message.c_str());
    }

    std::string Trim(std::string value)
    {
        const auto first = value.find_first_not_of(" \t\r\n");
        if (first == std::string::npos)
            return {};
        const auto last = value.find_last_not_of(" \t\r\n");
        return value.substr(first, last - first + 1);
    }

    bool ParseBool(const std::string &value, bool fallback)
    {
        std::string v = value;
        std::transform(v.begin(), v.end(), v.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
        if (v == "1" || v == "true" || v == "yes" || v == "on") return true;
        if (v == "0" || v == "false" || v == "no" || v == "off") return false;
        return fallback;
    }

    std::filesystem::path ResolveConfiguredPath(const std::string &value, const std::filesystem::path &fallback)
    {
        if (value.empty())
            return fallback;
        std::filesystem::path p = std::filesystem::u8path(value);
        if (p.is_relative())
            p = g_addon_directory / p;
        return p.lexically_normal();
    }

    void LoadConfiguration()
    {
        const auto config_path = g_addon_directory / L"FSR4-GFX803.ini";
        std::ifstream input(config_path);
        std::string line;
        std::string section;
        std::unordered_map<std::string, std::string> values;

        while (std::getline(input, line))
        {
            line = Trim(line);
            if (line.empty() || line[0] == ';' || line[0] == '#')
                continue;
            if (line.front() == '[' && line.back() == ']')
            {
                section = line.substr(1, line.size() - 2);
                continue;
            }
            const auto equal = line.find('=');
            if (equal == std::string::npos)
                continue;
            if (section == "FSR4GFX803")
                values[Trim(line.substr(0, equal))] = Trim(line.substr(equal + 1));
        }

        auto get = [&](const char *key, const char *fallback = "") -> std::string {
            const auto it = values.find(key);
            return it == values.end() ? fallback : it->second;
        };

        g_enabled = ParseBool(get("Enabled", "1"), true);
        g_dump_shaders = ParseBool(get("DumpShaders", "0"), false);
        g_verbose = ParseBool(get("VerboseLog", "0"), false);
        g_only_fsr4 = ParseBool(get("OnlyFsr4Shaders", "1"), true);
        g_override_directory = ResolveConfiguredPath(get("OverrideDirectory", "fsr4-overrides"), g_addon_directory / L"fsr4-overrides");
        g_dump_directory = ResolveConfiguredPath(get("DumpDirectory", "fsr4-overrides/dump"), g_addon_directory / L"fsr4-overrides" / L"dump");

        std::error_code ec;
        if (g_dump_shaders)
            std::filesystem::create_directories(g_dump_directory, ec);
        if (!g_override_directory.empty() && !std::filesystem::exists(g_override_directory, ec))
            std::filesystem::create_directories(g_override_directory, ec);

        Log(reshade::log::level::info,
            "FSR4 GFX803: " + std::string(g_enabled ? "enabled" : "disabled") +
            ", overrides='" + g_override_directory.string() +
            "', dump=" + (g_dump_shaders ? "on" : "off") +
            ", FSR4-only=" + (g_only_fsr4 ? "on" : "off"));
    }

    // The 16-byte DXBC container hash in bytes 4..19 is the stable key used by the shader packs.
    std::string ContainerHash(const void *code, size_t size)
    {
        if (code == nullptr || size < kMinDxilContainerSize)
            return {};
        const auto *bytes = static_cast<const uint8_t *>(code);
        if (std::memcmp(bytes, "DXBC", 4) != 0)
            return {};

        char text[33]{};
        for (int i = 0; i < 16; ++i)
            std::snprintf(text + i * 2, 3, "%02x", bytes[4 + i]);
        return text;
    }

    bool IsFsr4ModelShader(const void *code, size_t size)
    {
        static constexpr char marker[] = "fsr4_model_";
        if (code == nullptr || size < sizeof(marker) - 1)
            return false;

        const auto *bytes = static_cast<const char *>(code);
        const size_t marker_size = sizeof(marker) - 1;
        for (size_t i = 0; i + marker_size <= size; ++i)
        {
            if (bytes[i] == marker[0] && std::memcmp(bytes + i, marker, marker_size) == 0)
                return true;
        }
        return false;
    }

    void DumpOriginal(const std::string &hash, const void *code, size_t size)
    {
        if (!g_dump_shaders || hash.empty() || code == nullptr || size > kMaxShaderBytes)
            return;

        std::error_code ec;
        std::filesystem::create_directories(g_dump_directory, ec);
        if (ec)
        {
            Log(reshade::log::level::warning, "FSR4 GFX803: could not create shader dump directory: " + ec.message());
            return;
        }

        const auto path = g_dump_directory / (hash + ".dxil");
        if (std::filesystem::exists(path, ec))
            return;

        std::ofstream file(path, std::ios::binary | std::ios::out);
        if (!file)
        {
            Log(reshade::log::level::warning, "FSR4 GFX803: could not write shader dump " + path.string());
            return;
        }
        file.write(static_cast<const char *>(code), static_cast<std::streamsize>(size));
        if (file.good() && g_verbose)
            Log(reshade::log::level::info, "FSR4 GFX803: dumped " + hash + ".dxil");
    }

    const std::vector<uint8_t> *FindReplacement(const std::string &hash)
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        auto it = g_replacement_cache.find(hash);
        if (it == g_replacement_cache.end())
        {
            std::vector<uint8_t> data;
            const auto path = g_override_directory / (hash + ".dxil");
            std::ifstream file(path, std::ios::binary | std::ios::ate);
            if (file)
            {
                const std::streamsize size = file.tellg();
                if (size >= static_cast<std::streamsize>(kMinDxilContainerSize) &&
                    size <= static_cast<std::streamsize>(kMaxShaderBytes))
                {
                    data.resize(static_cast<size_t>(size));
                    file.seekg(0, std::ios::beg);
                    if (!file.read(reinterpret_cast<char *>(data.data()), size))
                        data.clear();
                }
                else
                {
                    g_invalid.fetch_add(1, std::memory_order_relaxed);
                }

                if (!data.empty() && std::memcmp(data.data(), "DXBC", 4) != 0)
                {
                    data.clear();
                    g_invalid.fetch_add(1, std::memory_order_relaxed);
                }
            }

            it = g_replacement_cache.emplace(hash, std::move(data)).first;
        }

        return it->second.empty() ? nullptr : &it->second;
    }

    bool OnCreatePipeline(device *, pipeline_layout, uint32_t count, const pipeline_subobject *subobjects)
    {
        if (!g_enabled || subobjects == nullptr)
            return false;

        bool replaced = false;
        for (uint32_t i = 0; i < count; ++i)
        {
            if (subobjects[i].type != pipeline_subobject_type::compute_shader ||
                subobjects[i].data == nullptr)
                continue;

            auto *shaders = static_cast<shader_desc *>(subobjects[i].data);
            for (uint32_t k = 0; k < subobjects[i].count; ++k)
            {
                shader_desc &shader = shaders[k];
                const std::string hash = ContainerHash(shader.code, shader.code_size);
                if (hash.empty())
                    continue;

                const bool is_fsr4 = IsFsr4ModelShader(shader.code, shader.code_size);
                if (g_only_fsr4 && !is_fsr4)
                    continue;

                if (is_fsr4)
                {
                    g_fsr4_seen.fetch_add(1, std::memory_order_relaxed);
                    DumpOriginal(hash, shader.code, shader.code_size);
                }

                if (const auto *replacement = FindReplacement(hash))
                {
                    shader.code = replacement->data();
                    shader.code_size = replacement->size();
                    replaced = true;
                    if (is_fsr4)
                        g_replaced.fetch_add(1, std::memory_order_relaxed);
                    Log(reshade::log::level::info, "FSR4 GFX803: replacing compute shader " + hash);
                }
                else if (is_fsr4)
                {
                    g_missing.fetch_add(1, std::memory_order_relaxed);
                    if (g_verbose)
                        Log(reshade::log::level::debug, "FSR4 GFX803: no replacement for " + hash);
                }
            }
        }

        return replaced;
    }
}

extern "C" __declspec(dllexport) const char *NAME = "FSR4 GFX803 DXIL Bridge";
extern "C" __declspec(dllexport) const char *DESCRIPTION =
    "Per-game FSR4 DXIL shader replacement and FSR4 shader dump bridge for GFX803 research.";

// ReShade calls AddonInit after LoadLibrary has returned, outside the Windows loader lock.
// Keep filesystem access and event registration out of DllMain.
extern "C" __declspec(dllexport) bool AddonInit(HMODULE module, HMODULE reshade_module)
{
    if (!reshade::register_addon(module, reshade_module))
        return false;

    wchar_t path[MAX_PATH]{};
    if (GetModuleFileNameW(module, path, MAX_PATH) == 0)
    {
        reshade::unregister_addon(module, reshade_module);
        return false;
    }

    g_addon_directory = std::filesystem::path(path).parent_path();
    LoadConfiguration();
    reshade::register_event<reshade::addon_event::create_pipeline>(OnCreatePipeline);
    return true;
}

extern "C" __declspec(dllexport) void AddonUninit(HMODULE module, HMODULE reshade_module)
{
    reshade::unregister_addon(module, reshade_module);
}

BOOL APIENTRY DllMain(HMODULE, DWORD, LPVOID)
{
    return TRUE;
}
