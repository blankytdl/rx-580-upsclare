// SPDX-License-Identifier: GPL-2.0-or-later
// DXIL assembler/validator for Windows. A small LoadLibraryW-based port of the helper
// in zgauthier2000/fsr4-shader-performance-overrides/windows/tools/dxilasm.cpp.
// Usage: dxilasm-win.exe <dxcompiler.dll> <input.ll> <output.dxil>

#include <Windows.h>
#include <dxcapi.h>

#include <cstdio>
#include <fstream>
#include <iterator>
#include <string>
#include <filesystem>

template <typename T>
class ComHolder
{
public:
    ComHolder() = default;
    ~ComHolder() { reset(); }
    ComHolder(const ComHolder &) = delete;
    ComHolder &operator=(const ComHolder &) = delete;
    T **put() { reset(); return &value_; }
    T *get() const { return value_; }
    T *operator->() const { return value_; }
    void reset(T *value = nullptr) { if (value_) value_->Release(); value_ = value; }
private:
    T *value_ = nullptr;
};

static void PrintBlob(IDxcBlobEncoding *blob)
{
    if (blob && blob->GetBufferSize() != 0)
        std::fwrite(blob->GetBufferPointer(), 1, blob->GetBufferSize(), stderr);
}

static HRESULT ReadStatus(IDxcOperationResult *result, const char *what)
{
    if (result == nullptr)
        return E_FAIL;
    ComHolder<IDxcBlobEncoding> errors;
    result->GetErrorBuffer(errors.put());
    PrintBlob(errors.get());
    HRESULT status = E_FAIL;
    result->GetStatus(&status);
    if (FAILED(status))
        std::fprintf(stderr, "%s failed: 0x%08X\n", what, static_cast<unsigned>(status));
    return status;
}

int main(int argc, char **argv)
{
    if (argc != 4)
    {
        std::fprintf(stderr, "usage: dxilasm-win.exe <dxcompiler.dll> <input.ll> <output.dxil>\n");
        return 2;
    }

    const std::filesystem::path compiler_path = std::filesystem::u8path(argv[1]);
    const std::filesystem::path input_path = std::filesystem::u8path(argv[2]);
    const std::filesystem::path output_path = std::filesystem::u8path(argv[3]);

    HMODULE library = LoadLibraryExW(compiler_path.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    if (library == nullptr)
    {
        std::fprintf(stderr, "could not load DXC library: %lu\n", GetLastError());
        return 2;
    }

    auto create = reinterpret_cast<DxcCreateInstanceProc>(GetProcAddress(library, "DxcCreateInstance"));
    if (create == nullptr)
    {
        std::fprintf(stderr, "DxcCreateInstance export not found\n");
        return 2;
    }

    std::ifstream input(input_path, std::ios::binary);
    std::string ir((std::istreambuf_iterator<char>(input)), std::istreambuf_iterator<char>());
    if (ir.empty())
    {
        std::fprintf(stderr, "could not read input IR: %s\n", argv[2]);
        return 2;
    }

    ComHolder<IDxcUtils> utils;
    ComHolder<IDxcAssembler> assembler;
    ComHolder<IDxcValidator> validator;
    HRESULT hr = create(CLSID_DxcUtils, __uuidof(IDxcUtils), reinterpret_cast<void **>(utils.put()));
    if (FAILED(hr))
    {
        std::fprintf(stderr, "DxcCreateInstance(IDxcUtils) failed: 0x%08X\n", static_cast<unsigned>(hr));
        return 3;
    }
    hr = create(CLSID_DxcAssembler, __uuidof(IDxcAssembler), reinterpret_cast<void **>(assembler.put()));
    if (FAILED(hr))
    {
        std::fprintf(stderr, "DxcCreateInstance(IDxcAssembler) failed: 0x%08X\n", static_cast<unsigned>(hr));
        return 3;
    }
    hr = create(CLSID_DxcValidator, __uuidof(IDxcValidator), reinterpret_cast<void **>(validator.put()));
    if (FAILED(hr))
    {
        std::fprintf(stderr, "DxcCreateInstance(IDxcValidator) failed: 0x%08X\n", static_cast<unsigned>(hr));
        return 3;
    }

    ComHolder<IDxcBlobEncoding> source;
    hr = utils->CreateBlob(ir.data(), static_cast<UINT32>(ir.size()), DXC_CP_UTF8, source.put());
    if (FAILED(hr))
    {
        std::fprintf(stderr, "CreateBlob failed: 0x%08X\n", static_cast<unsigned>(hr));
        return 4;
    }

    ComHolder<IDxcOperationResult> assemble_result;
    hr = assembler->AssembleToContainer(source.get(), assemble_result.put());
    if (FAILED(hr) || FAILED(ReadStatus(assemble_result.get(), "DXIL assembly")))
    {
        return 5;
    }

    ComHolder<IDxcBlob> container;
    hr = assemble_result->GetResult(container.put());
    if (FAILED(hr) || container.get() == nullptr)
    {
        std::fprintf(stderr, "DXIL assembler returned no container\n");
        return 5;
    }

    ComHolder<IDxcOperationResult> validation_result;
    hr = validator->Validate(container.get(), DxcValidatorFlags_InPlaceEdit, validation_result.put());
    if (FAILED(hr) || FAILED(ReadStatus(validation_result.get(), "DXIL validation")))
    {
        return 6;
    }

    std::ofstream output(output_path, std::ios::binary);
    output.write(static_cast<const char *>(container->GetBufferPointer()),
                 static_cast<std::streamsize>(container->GetBufferSize()));
    const bool written = output.good();
    output.close();
    FreeLibrary(library);

    if (!written)
    {
        std::fprintf(stderr, "could not write output DXIL: %s\n", argv[3]);
        return 7;
    }

    std::printf("validated DXIL: %s (%zu bytes)\n", argv[3], static_cast<size_t>(container->GetBufferSize()));
    return 0;
}
