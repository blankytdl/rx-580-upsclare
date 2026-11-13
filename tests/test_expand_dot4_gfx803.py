import unittest
from tools.windows.dxil.expand_dot4_gfx803 import expand_text

class ExpandDot4Tests(unittest.TestCase):
    def test_expands_accumulating_dot(self):
        ir = "declare i32 @dx.op.dot4AddPacked.i32(i32, i32, i32, i32)\n%r = call i32 @dx.op.dot4AddPacked.i32(i32 163, i32 %acc, i32 %a, i32 %b)\n"
        out, count = expand_text(ir)
        self.assertEqual(count, 1)
        self.assertNotIn("@dx.op.dot4AddPacked", out)
        self.assertNotIn("declare i32 @dx.op.dot4AddPacked", out)
        self.assertEqual(out.count(" = mul i32 "), 4)
        self.assertEqual(out.count(" = ashr i32 "), 8)
        self.assertIn("%r = add i32 ", out)

    def test_preserves_ir_without_packed_dot(self):
        ir = "define void @f() {\n  ret void\n}\n"
        out, count = expand_text(ir)
        self.assertEqual(count, 0)
        self.assertEqual(out, ir)

    def test_multiple_dot_products_get_unique_ssa_names(self):
        ir = (
            "%a = call i32 @dx.op.dot4AddPacked.i32(i32 163, i32 0, i32 %x, i32 %y)\n"
            "%b = call i32 @dx.op.dot4AddPacked.i32(i32 163, i32 %a, i32 %x, i32 %y)\n"
        )
        out, count = expand_text(ir)
        self.assertEqual(count, 2)
        self.assertEqual(out.count(" = mul i32 "), 8)

if __name__ == "__main__":
    unittest.main()
