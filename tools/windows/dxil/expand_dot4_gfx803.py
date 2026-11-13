#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-2.0-or-later
"""
Expand DXIL's signed packed-int8 dot product into explicit signed-byte extracts
and 32-bit multiply/add operations.

Input is LLVM IR text produced by:
    dxc -dumpbin shader.dxil

The transform is algebraically exact for signed 4x8 dot products:
    acc + sum(signed_byte(a[i]) * signed_byte(b[i]), i=0..3)

This is a source-level lowering experiment. It does NOT guarantee that AMD's
Windows compiler will emit v_mad_i32_i24. Inspect the actual GFX803 ISA with
Radeon GPU Analyzer before treating a transformed shader as an optimization.
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

DOT_RE = re.compile(
    r"^(\s*)(%[-\w.$]+)\s*=\s*(?:(?:tail|musttail|notail)\s+)?"
    r"call\s+i32\s+@dx\.op\.dot4AddPacked\.i32"
    r"\(i32\s+163,\s*i32\s+([^,]+),\s*i32\s+([^,]+),\s*i32\s+([^)]+)\)"
)

DOT_DECL_RE = re.compile(
    r"^\s*declare\b.*@dx\.op\.dot4AddPacked\.i32\([^)]*\).*$"
)

def _emit_extract(lines: list[str], indent: str, prefix: str, value: str, lane: int) -> str:
    shift = 24 - lane * 8
    if shift:
        shifted = f"%{prefix}.shl"
        lines.append(f"{indent}{shifted} = shl i32 {value}, {shift}")
        source = shifted
    else:
        source = value
    result = f"%{prefix}.sext"
    lines.append(f"{indent}{result} = ashr i32 {source}, 24")
    return result

def expand_text(text: str) -> tuple[str, int]:
    lines_out: list[str] = []
    count = 0
    occupied = set(re.findall(r"%[-\w.$]+", text))

    for line in text.splitlines():
        match = DOT_RE.match(line)
        if not match:
            lines_out.append(line)
            continue

        indent, result_id, accumulator, packed_a, packed_b = match.groups()
        count += 1
        base = f"fsr4gfx803.dot4.{count}"
        suffix = 0
        while any(f"%{base}.{suffix}.{part}" in occupied for part in ("a", "b", "p")):
            suffix += 1
        if suffix:
            base = f"{base}.{suffix}"

        a_values = [
            _emit_extract(lines_out, indent, f"{base}.a{i}", packed_a.strip(), i)
            for i in range(4)
        ]
        b_values = [
            _emit_extract(lines_out, indent, f"{base}.b{i}", packed_b.strip(), i)
            for i in range(4)
        ]

        products: list[str] = []
        for i in range(4):
            product = f"%{base}.p{i}"
            lines_out.append(f"{indent}{product} = mul i32 {a_values[i]}, {b_values[i]}")
            products.append(product)

        sum01 = f"%{base}.sum01"
        sum012 = f"%{base}.sum012"
        sum0123 = f"%{base}.sum0123"
        lines_out.append(f"{indent}{sum01} = add i32 {products[0]}, {products[1]}")
        lines_out.append(f"{indent}{sum012} = add i32 {sum01}, {products[2]}")
        lines_out.append(f"{indent}{sum0123} = add i32 {sum012}, {products[3]}")
        lines_out.append(f"{indent}{result_id} = add i32 {sum0123}, {accumulator.strip()}")

    # DXIL validation rejects an unused declaration for this external intrinsic.
    # Remove it only when all packed-dot call sites were rewritten.
    if count and not any(DOT_RE.match(line) for line in lines_out):
        lines_out = [line for line in lines_out if not DOT_DECL_RE.match(line)]

    return "\n".join(lines_out) + ("\n" if text.endswith("\n") else ""), count

def self_test() -> None:
    sample = """declare i32 @dx.op.dot4AddPacked.i32(i32, i32, i32, i32)

define i32 @probe(i32 %acc, i32 %a, i32 %b) {
entry:
  %v = call i32 @dx.op.dot4AddPacked.i32(i32 163, i32 %acc, i32 %a, i32 %b)
  ret i32 %v
}
"""
    result, count = expand_text(sample)
    assert count == 1, f"expected one expanded dot product, got {count}"
    assert "@dx.op.dot4AddPacked.i32" not in result
    assert result.count(" = mul i32 ") == 4
    assert result.count(" = ashr i32 ") == 8
    assert "%v = add i32 " in result
    print("PASS: one DXIL packed dot product expanded to four signed byte products")

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", nargs="?", help="DXIL LLVM IR text dumped by dxc -dumpbin")
    parser.add_argument("output", nargs="?", help="output LLVM IR text path")
    parser.add_argument("--allow-none", action="store_true", help="allow inputs without matching dot4AddPacked calls")
    parser.add_argument("--self-test", action="store_true", help="run the built-in transformation test")
    args = parser.parse_args()

    if args.self_test:
        self_test()
        return 0
    if not args.input or not args.output:
        parser.error("input and output are required unless --self-test is used")

    source = Path(args.input).read_text(encoding="utf-8-sig")
    rewritten, count = expand_text(source)
    if count == 0 and not args.allow_none:
        print("expand_dot4_gfx803: no dx.op.dot4AddPacked.i32 calls found; refusing to create a misleading output", file=sys.stderr)
        return 2
    Path(args.output).write_text(rewritten, encoding="utf-8", newline="\n")
    print(f"expand_dot4_gfx803: expanded {count} packed signed-int8 dot products", file=sys.stderr)
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
