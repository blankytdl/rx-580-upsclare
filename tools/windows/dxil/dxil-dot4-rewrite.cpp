// SPDX-License-Identifier: GPL-2.0-or-later
// Native Windows DXIL text transformer for GFX803 experiments.
// Usage: dxil-dot4-rewrite.exe <input.ll> <output.ll>
//
// Expands each signed DXIL dot4AddPacked operation into explicit signed-byte
// extraction, multiply and 32-bit addition. The arithmetic is bit-exact modulo
// 2^32, but the AMD Windows compiler's emitted GFX803 ISA must still be checked
// with Radeon GPU Analyzer before the result can be called an optimization.

#include <algorithm>
#include <fstream>
#include <iostream>
#include <regex>
#include <string>
#include <vector>

namespace
{
    const std::regex kDotPattern(
        R"(^(s*)(%[-w.$]+)s*=s*(tails+|musttails+|notails+)?calls+i32s+@dx.op.dot4AddPacked.i32(i32s+163,s*i32s+([^,]+),s*i32s+([^,]+),s*i32s+([^)]+)))",
        std::regex::ECMAScript);
    const std::regex kDeclarationPattern(
        R"(^s*declare.*@dx.op.dot4AddPacked.i32([^)]*).*$)",
        std::regex::ECMAScript);

    std::string Trim(const std::string &value)
    {
        const auto first = value.find_first_not_of(" \t\r\n");
        if (first == std::string::npos)
            return {};
        const auto last = value.find_last_not_of(" \t\r\n");
        return value.substr(first, last - first + 1);
    }

    void EmitExtract(std::vector<std::string> &output,
                     const std::string &indent,
                     const std::string &name,
                     const std::string &value,
                     int lane)
    {
        const int shift = 24 - lane * 8;
        std::string source = value;
        if (shift != 0)
        {
            const std::string shifted = "%" + name + ".shl";
            output.push_back(indent + shifted + " = shl i32 " + value + ", " + std::to_string(shift));
            source = shifted;
        }
        output.push_back(indent + "%" + name + ".sext = ashr i32 " + source + ", 24");
    }
}

int main(int argc, char **argv)
{
    if (argc != 3)
    {
        std::cerr << "Usage: dxil-dot4-rewrite.exe <input.ll> <output.ll>\n";
        return 2;
    }

    std::ifstream input(argv[1], std::ios::binary);
    if (!input)
    {
        std::cerr << "Could not open input IR: " << argv[1] << "\n";
        return 2;
    }

    std::vector<std::string> original;
    std::string line;
    while (std::getline(input, line))
        original.push_back(line);
    if (input.bad())
    {
        std::cerr << "Failed while reading input IR: " << argv[1] << "\n";
        return 2;
    }

    std::string allText;
    for (const auto &entry : original)
        allText += entry + '\n';

    std::vector<std::string> output;
    output.reserve(original.size() + 32);
    size_t replacements = 0;

    for (const std::string &sourceLine : original)
    {
        std::smatch match;
        if (!std::regex_search(sourceLine, match, kDotPattern))
        {
            output.push_back(sourceLine);
            continue;
        }

        const std::string indent = match[1].str();
        const std::string resultId = match[2].str();
        const std::string accumulator = Trim(match[4].str());
        const std::string packedA = Trim(match[5].str());
        const std::string packedB = Trim(match[6].str());

        ++replacements;
        std::string base = "fsr4gfx803.dot4." + std::to_string(replacements);
        for (unsigned suffix = 0; allText.find("%" + base + ".") != std::string::npos; ++suffix)
            base = "fsr4gfx803.dot4." + std::to_string(replacements) + ".u" + std::to_string(suffix + 1);

        std::vector<std::string> aValues;
        std::vector<std::string> bValues;
        for (int lane = 0; lane < 4; ++lane)
        {
            const std::string aName = base + ".a" + std::to_string(lane);
            const std::string bName = base + ".b" + std::to_string(lane);
            EmitExtract(output, indent, aName, packedA, lane);
            EmitExtract(output, indent, bName, packedB, lane);
            aValues.push_back("%" + aName + ".sext");
            bValues.push_back("%" + bName + ".sext");
        }

        std::vector<std::string> products;
        for (int lane = 0; lane < 4; ++lane)
        {
            const std::string product = "%" + base + ".p" + std::to_string(lane);
            output.push_back(indent + product + " = mul i32 " + aValues[lane] + ", " + bValues[lane]);
            products.push_back(product);
        }

        const std::string sum01 = "%" + base + ".sum01";
        const std::string sum012 = "%" + base + ".sum012";
        const std::string sum0123 = "%" + base + ".sum0123";
        output.push_back(indent + sum01 + " = add i32 " + products[0] + ", " + products[1]);
        output.push_back(indent + sum012 + " = add i32 " + sum01 + ", " + products[2]);
        output.push_back(indent + sum0123 + " = add i32 " + sum012 + ", " + products[3]);
        output.push_back(indent + resultId + " = add i32 " + sum0123 + ", " + accumulator);
    }

    bool hasUnexpandedCall = false;
    for (const auto &entry : output)
    {
        if (std::regex_search(entry, kDotPattern))
        {
            hasUnexpandedCall = true;
            break;
        }
    }

    if (replacements == 0)
    {
        std::cerr << "No dx.op.dot4AddPacked.i32 calls found; no output written.\n";
        return 2;
    }

    if (!hasUnexpandedCall)
    {
        output.erase(std::remove_if(output.begin(), output.end(),
            [](const std::string &entry) { return std::regex_match(entry, kDeclarationPattern); }),
            output.end());
    }

    std::ofstream out(argv[2], std::ios::binary | std::ios::trunc);
    if (!out)
    {
        std::cerr << "Could not open output IR: " << argv[2] << "\n";
        return 3;
    }
    for (const auto &entry : output)
        out << entry << '\n';
    if (!out.good())
    {
        std::cerr << "Failed while writing output IR: " << argv[2] << "\n";
        return 3;
    }

    std::cerr << "Expanded " << replacements
              << " signed packed-int8 dot product(s).\n";
    return 0;
}
