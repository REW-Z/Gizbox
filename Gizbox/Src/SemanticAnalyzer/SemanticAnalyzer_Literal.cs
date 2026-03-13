using System;
using System.Collections.Generic;
using System.Text;

namespace Gizbox.SemanticRule
{
    public partial class SemanticAnalyzer
    {

        private bool TryFoldUnaryConstant(SyntaxTree.UnaryOpNode unaryNode, out SyntaxTree.LiteralNode foldedNode)
        {
            foldedNode = null;

            if(TryGetLiteralConstant(unaryNode.exprNode, out var operandTypeExpr, out var operandValue) == false)
                return false;

            var operandType = GType.Parse(operandTypeExpr);
            object resultValue = null;
            string resultTypeExpr = operandTypeExpr;

            switch(operandType.Category)
            {
                case GType.Kind.Bool:
                    {
                        bool v = (bool)operandValue;
                        switch(unaryNode.op)
                        {
                            case "!":
                                resultValue = !v;
                                break;
                            default:
                                return false;
                        }
                    }
                    break;
                case GType.Kind.Byte:
                    {
                        byte v = (byte)operandValue;
                        switch(unaryNode.op)
                        {
                            case "!":
                                resultValue = (byte)(v == 0 ? 1 : 0);
                                break;
                            case "~":
                                resultValue = unchecked((byte)~v);
                                break;
                            case "NEG":
                                resultValue = unchecked((byte)(-v));
                                break;
                            default:
                                return false;
                        }
                    }
                    break;
                case GType.Kind.Char:
                    {
                        char v = (char)operandValue;
                        switch(unaryNode.op)
                        {
                            case "!":
                                resultValue = (char)(v == '\0' ? 1 : 0);
                                break;
                            case "~":
                                resultValue = unchecked((char)~v);
                                break;
                            case "NEG":
                                resultValue = unchecked((char)(-(int)v));
                                break;
                            default:
                                return false;
                        }
                    }
                    break;
                case GType.Kind.Int:
                    {
                        int v = (int)operandValue;
                        switch(unaryNode.op)
                        {
                            case "!":
                                resultValue = v == 0 ? 1 : 0;
                                break;
                            case "~":
                                resultValue = ~v;
                                break;
                            case "NEG":
                                resultValue = unchecked(-v);
                                break;
                            default:
                                return false;
                        }
                    }
                    break;
                case GType.Kind.UInt:
                    {
                        uint v = (uint)operandValue;
                        switch(unaryNode.op)
                        {
                            case "!":
                                resultValue = v == 0 ? 1u : 0u;
                                break;
                            case "~":
                                resultValue = ~v;
                                break;
                            case "NEG":
                                resultValue = unchecked((uint)(-v));
                                break;
                            default:
                                return false;
                        }
                    }
                    break;
                case GType.Kind.Long:
                    {
                        long v = (long)operandValue;
                        switch(unaryNode.op)
                        {
                            case "!":
                                resultValue = v == 0 ? 1L : 0L;
                                break;
                            case "~":
                                resultValue = ~v;
                                break;
                            case "NEG":
                                resultValue = unchecked(-v);
                                break;
                            default:
                                return false;
                        }
                    }
                    break;
                case GType.Kind.ULong:
                    {
                        ulong v = (ulong)operandValue;
                        switch(unaryNode.op)
                        {
                            case "!":
                                resultValue = v == 0 ? 1ul : 0ul;
                                break;
                            case "~":
                                resultValue = ~v;
                                break;
                            case "NEG":
                                resultValue = unchecked((ulong)(-(long)v));
                                break;
                            default:
                                return false;
                        }
                    }
                    break;
                case GType.Kind.Float:
                    {
                        float v = (float)operandValue;
                        if(unaryNode.op != "NEG")
                            return false;

                        var r = -v;
                        if(float.IsNaN(r) || float.IsInfinity(r))
                            return false;

                        resultValue = r;
                    }
                    break;
                case GType.Kind.Double:
                    {
                        double v = (double)operandValue;
                        if(unaryNode.op != "NEG")
                            return false;

                        var r = -v;
                        if(double.IsNaN(r) || double.IsInfinity(r))
                            return false;

                        resultValue = r;
                    }
                    break;
                default:
                    return false;
            }

            foldedNode = CreateFoldedLiteralNode(resultTypeExpr, resultValue, unaryNode);
            return foldedNode != null;
        }


        private bool TryFoldBinaryConstant(SyntaxTree.BinaryOpNode binaryNode, out SyntaxTree.LiteralNode foldedNode)
        {
            foldedNode = null;

            if(TryGetLiteralConstant(binaryNode.leftNode, out var leftTypeExpr, out var leftValue) == false
                || TryGetLiteralConstant(binaryNode.rightNode, out var rightTypeExpr, out var rightValue) == false)
            {
                return false;
            }

            //binaryNode.op != "<<" && binaryNode.op != ">>"
            if(binaryNode.op != "<<" && binaryNode.op != ">>" && CheckType_Equal(leftTypeExpr, rightTypeExpr) == false)
            {
                return false;
            }

            var leftType = GType.Parse(leftTypeExpr);
            var isShiftOp = binaryNode.op == "<<" || binaryNode.op == ">>";
            int shiftAmount = 0;

            if(isShiftOp)
            {
                var rightType = GType.Parse(rightTypeExpr);
                if(rightType.IsInteger == false)
                    return false;

                switch(rightType.Category)
                {
                    case GType.Kind.Byte:
                        shiftAmount = (byte)rightValue;
                        break;
                    case GType.Kind.Int:
                        shiftAmount = (int)rightValue;
                        break;
                    case GType.Kind.UInt:
                        shiftAmount = unchecked((int)(uint)rightValue);
                        break;
                    case GType.Kind.Long:
                        shiftAmount = unchecked((int)(long)rightValue);
                        break;
                    case GType.Kind.ULong:
                        shiftAmount = unchecked((int)(ulong)rightValue);
                        break;
                    case GType.Kind.Enum:
                        shiftAmount = (int)rightValue;
                        break;
                    default:
                        return false;
                }
            }

            object resultValue = null;
            string resultTypeExpr = binaryNode.IsCompare ? "bool" : leftTypeExpr;


            switch(leftType.Category)
            {
                case GType.Kind.Bool:
                    {
                        bool l = (bool)leftValue;
                        bool r = (bool)rightValue;
                        switch(binaryNode.op)
                        {
                            case "==":
                                resultValue = l == r;
                                break;
                            case "!=":
                                resultValue = l != r;
                                break;
                            case "&":
                                resultValue = l & r;
                                break;
                            case "|":
                                resultValue = l | r;
                                break;
                            default:
                                return false;
                        }
                    }
                    break;
                case GType.Kind.Byte:
                    {
                        byte l = (byte)leftValue;
                        byte r = (byte)rightValue;
                        switch(binaryNode.op)
                        {
                            case "+":
                                resultValue = unchecked((byte)(l + r));
                                break;
                            case "-":
                                resultValue = unchecked((byte)(l - r));
                                break;
                            case "*":
                                resultValue = unchecked((byte)(l * r));
                                break;
                            case "/":
                                if(r == 0)
                                    return false;
                                resultValue = unchecked((byte)(l / r));
                                break;
                            case "%":
                                if(r == 0)
                                    return false;
                                resultValue = unchecked((byte)(l % r));
                                break;
                            case "&":
                                resultValue = unchecked((byte)(l & r));
                                break;
                            case "|":
                                resultValue = unchecked((byte)(l | r));
                                break;
                            case "==":
                                resultValue = l == r;
                                break;
                            case "!=":
                                resultValue = l != r;
                                break;
                            case "<":
                                resultValue = l < r;
                                break;
                            case "<=":
                                resultValue = l <= r;
                                break;
                            case ">":
                                resultValue = l > r;
                                break;
                            case ">=":
                                resultValue = l >= r;
                                break;
                            default:
                                return false;
                        }
                    }
                    break;
                case GType.Kind.Char:
                    {
                        char l = (char)leftValue;
                        char r = (char)rightValue;
                        switch(binaryNode.op)
                        {
                            case "+":
                                resultValue = unchecked((char)(l + r));
                                break;
                            case "-":
                                resultValue = unchecked((char)(l - r));
                                break;
                            case "*":
                                resultValue = unchecked((char)(l * r));
                                break;
                            case "/":
                                if(r == '\0')
                                    return false;
                                resultValue = unchecked((char)(l / r));
                                break;
                            case "%":
                                if(r == '\0')
                                    return false;
                                resultValue = unchecked((char)(l % r));
                                break;
                            case "&":
                                resultValue = unchecked((char)(l & r));
                                break;
                            case "|":
                                resultValue = unchecked((char)(l | r));
                                break;
                            case "==":
                                resultValue = l == r;
                                break;
                            case "!=":
                                resultValue = l != r;
                                break;
                            case "<":
                                resultValue = l < r;
                                break;
                            case "<=":
                                resultValue = l <= r;
                                break;
                            case ">":
                                resultValue = l > r;
                                break;
                            case ">=":
                                resultValue = l >= r;
                                break;
                            default:
                                return false;
                        }
                    }
                    break;
                case GType.Kind.Int:
                    {
                        int l = (int)leftValue;
                        int r = isShiftOp ? 0 : (int)rightValue;
                        switch(binaryNode.op)
                        {
                            case "+":
                                resultValue = unchecked(l + r);
                                break;
                            case "-":
                                resultValue = unchecked(l - r);
                                break;
                            case "*":
                                resultValue = unchecked(l * r);
                                break;
                            case "/":
                                if(r == 0)
                                    return false;
                                resultValue = l / r;
                                break;
                            case "%":
                                if(r == 0)
                                    return false;
                                resultValue = l % r;
                                break;
                            case "<<":
                                resultValue = l << shiftAmount;
                                break;
                            case ">>":
                                resultValue = l >> shiftAmount;
                                break;
                            case "&":
                                resultValue = l & r;
                                break;
                            case "|":
                                resultValue = l | r;
                                break;
                            case "==":
                                resultValue = l == r;
                                break;
                            case "!=":
                                resultValue = l != r;
                                break;
                            case "<":
                                resultValue = l < r;
                                break;
                            case "<=":
                                resultValue = l <= r;
                                break;
                            case ">":
                                resultValue = l > r;
                                break;
                            case ">=":
                                resultValue = l >= r;
                                break;
                            default:
                                return false;
                        }
                    }
                    break;
                case GType.Kind.UInt:
                    {
                        uint l = (uint)leftValue;
                        uint r = isShiftOp ? 0u : (uint)rightValue;
                        switch(binaryNode.op)
                        {
                            case "+":
                                resultValue = unchecked(l + r);
                                break;
                            case "-":
                                resultValue = unchecked(l - r);
                                break;
                            case "*":
                                resultValue = unchecked(l * r);
                                break;
                            case "/":
                                if(r == 0)
                                    return false;
                                resultValue = l / r;
                                break;
                            case "%":
                                if(r == 0)
                                    return false;
                                resultValue = l % r;
                                break;
                            case "<<":
                                resultValue = l << shiftAmount;
                                break;
                            case ">>":
                                resultValue = l >> shiftAmount;
                                break;
                            case "&":
                                resultValue = l & r;
                                break;
                            case "|":
                                resultValue = l | r;
                                break;
                            case "==":
                                resultValue = l == r;
                                break;
                            case "!=":
                                resultValue = l != r;
                                break;
                            case "<":
                                resultValue = l < r;
                                break;
                            case "<=":
                                resultValue = l <= r;
                                break;
                            case ">":
                                resultValue = l > r;
                                break;
                            case ">=":
                                resultValue = l >= r;
                                break;
                            default:
                                return false;
                        }
                    }
                    break;
                case GType.Kind.Long:
                    {
                        long l = (long)leftValue;
                        long r = isShiftOp ? 0L : (long)rightValue;
                        switch(binaryNode.op)
                        {
                            case "+":
                                resultValue = unchecked(l + r);
                                break;
                            case "-":
                                resultValue = unchecked(l - r);
                                break;
                            case "*":
                                resultValue = unchecked(l * r);
                                break;
                            case "/":
                                if(r == 0)
                                    return false;
                                resultValue = l / r;
                                break;
                            case "%":
                                if(r == 0)
                                    return false;
                                resultValue = l % r;
                                break;
                            case "<<":
                                resultValue = l << shiftAmount;
                                break;
                            case ">>":
                                resultValue = l >> shiftAmount;
                                break;
                            case "&":
                                resultValue = l & r;
                                break;
                            case "|":
                                resultValue = l | r;
                                break;
                            case "==":
                                resultValue = l == r;
                                break;
                            case "!=":
                                resultValue = l != r;
                                break;
                            case "<":
                                resultValue = l < r;
                                break;
                            case "<=":
                                resultValue = l <= r;
                                break;
                            case ">":
                                resultValue = l > r;
                                break;
                            case ">=":
                                resultValue = l >= r;
                                break;
                            default:
                                return false;
                        }
                    }
                    break;
                case GType.Kind.ULong:
                    {
                        ulong l = (ulong)leftValue;
                        ulong r = isShiftOp ? 0ul : (ulong)rightValue;
                        switch(binaryNode.op)
                        {
                            case "+":
                                resultValue = unchecked(l + r);
                                break;
                            case "-":
                                resultValue = unchecked(l - r);
                                break;
                            case "*":
                                resultValue = unchecked(l * r);
                                break;
                            case "/":
                                if(r == 0)
                                    return false;
                                resultValue = l / r;
                                break;
                            case "%":
                                if(r == 0)
                                    return false;
                                resultValue = l % r;
                                break;
                            case "<<":
                                resultValue = l << shiftAmount;
                                break;
                            case ">>":
                                resultValue = l >> shiftAmount;
                                break;
                            case "&":
                                resultValue = l & r;
                                break;
                            case "|":
                                resultValue = l | r;
                                break;
                            case "==":
                                resultValue = l == r;
                                break;
                            case "!=":
                                resultValue = l != r;
                                break;
                            case "<":
                                resultValue = l < r;
                                break;
                            case "<=":
                                resultValue = l <= r;
                                break;
                            case ">":
                                resultValue = l > r;
                                break;
                            case ">=":
                                resultValue = l >= r;
                                break;
                            default:
                                return false;
                        }
                    }
                    break;
                case GType.Kind.Float:
                    {
                        float l = (float)leftValue;
                        float r = (float)rightValue;
                        switch(binaryNode.op)
                        {
                            case "+":
                                resultValue = l + r;
                                break;
                            case "-":
                                resultValue = l - r;
                                break;
                            case "*":
                                resultValue = l * r;
                                break;
                            case "/":
                                resultValue = l / r;
                                break;
                            case "==":
                                resultValue = l == r;
                                break;
                            case "!=":
                                resultValue = l != r;
                                break;
                            case "<":
                                resultValue = l < r;
                                break;
                            case "<=":
                                resultValue = l <= r;
                                break;
                            case ">":
                                resultValue = l > r;
                                break;
                            case ">=":
                                resultValue = l >= r;
                                break;
                            default:
                                return false;
                        }

                        if(resultValue is float fr && (float.IsNaN(fr) || float.IsInfinity(fr)))
                            return false;
                    }
                    break;
                case GType.Kind.Double:
                    {
                        double l = (double)leftValue;
                        double r = (double)rightValue;
                        switch(binaryNode.op)
                        {
                            case "+":
                                resultValue = l + r;
                                break;
                            case "-":
                                resultValue = l - r;
                                break;
                            case "*":
                                resultValue = l * r;
                                break;
                            case "/":
                                resultValue = l / r;
                                break;
                            case "==":
                                resultValue = l == r;
                                break;
                            case "!=":
                                resultValue = l != r;
                                break;
                            case "<":
                                resultValue = l < r;
                                break;
                            case "<=":
                                resultValue = l <= r;
                                break;
                            case ">":
                                resultValue = l > r;
                                break;
                            case ">=":
                                resultValue = l >= r;
                                break;
                            default:
                                return false;
                        }

                        if(resultValue is double dr && (double.IsNaN(dr) || double.IsInfinity(dr)))
                            return false;
                    }
                    break;
                case GType.Kind.Enum:
                    {
                        int l = (int)leftValue;
                        int r = (int)rightValue;
                        switch(binaryNode.op)
                        {
                            case "==":
                                resultValue = l == r;
                                break;
                            case "!=":
                                resultValue = l != r;
                                break;
                            case "<":
                                resultValue = l < r;
                                break;
                            case "<=":
                                resultValue = l <= r;
                                break;
                            case ">":
                                resultValue = l > r;
                                break;
                            case ">=":
                                resultValue = l >= r;
                                break;
                            default:
                                return false;
                        }
                    }
                    break;
                default:
                    return false;
            }

            foldedNode = CreateFoldedLiteralNode(resultTypeExpr, resultValue, binaryNode);
            return foldedNode != null;
        }

        private bool TryGetLiteralConstant(SyntaxTree.ExprNode exprNode, out string typeExpr, out object value)
        {
            typeExpr = null;
            value = null;

            exprNode = GetEffectiveExprNode(exprNode);
            if(exprNode is not SyntaxTree.LiteralNode literalNode)
                return false;

            typeExpr = literalNode.attributes != null && literalNode.attributes.TryGetValue(AstAttr.type, out var typeObj)
                ? typeObj as string
                : TypeUtils.GetLitType(literalNode.token);

            if(string.IsNullOrWhiteSpace(typeExpr) || typeExpr == "null")
                return false;

            var type = GType.Parse(typeExpr);
            var literalValue = literalNode.token?.attribute;
            if(literalValue == null)
                return false;

            switch(type.Category)
            {
                case GType.Kind.Bool:
                    value = bool.Parse(literalValue);
                    return true;
                case GType.Kind.Byte:
                    value = byte.Parse(TrimIntegerSuffix(literalValue));
                    return true;
                case GType.Kind.Char:
                    value = ParseCharLiteralValue(literalValue);
                    return true;
                case GType.Kind.Int:
                    value = int.Parse(literalValue);
                    return true;
                case GType.Kind.UInt:
                    value = uint.Parse(TrimIntegerSuffix(literalValue));
                    return true;
                case GType.Kind.Long:
                    value = long.Parse(TrimIntegerSuffix(literalValue));
                    return true;
                case GType.Kind.ULong:
                    value = ulong.Parse(TrimIntegerSuffix(literalValue));
                    return true;
                case GType.Kind.Float:
                    value = float.Parse(TrimFloatSuffix(literalValue));
                    return true;
                case GType.Kind.Double:
                    value = double.Parse(TrimDoubleSuffix(literalValue));
                    return true;
                case GType.Kind.Enum:
                    value = int.Parse(TrimIntegerSuffix(literalValue));
                    return true;
                default:
                    return false;
            }
        }

        private SyntaxTree.LiteralNode CreateFoldedLiteralNode(string typeExpr, object value, SyntaxTree.Node sourceNode)
        {
            var canonicalTypeExpr = CanonicalizeTypeExpression(typeExpr);
            var tokenName = TypeUtils.GetLitTokenName(canonicalTypeExpr);
            if(string.IsNullOrWhiteSpace(tokenName) || tokenName == "null")
                return null;

            var attr = FormatLiteralAttribute(GType.Parse(canonicalTypeExpr), value);
            if(attr == null)
                return null;

            var refToken = sourceNode?.StartToken() ?? sourceNode?.EndToken();

            return new SyntaxTree.LiteralNode()
            {
                token = new Token(tokenName, PatternType.Literal, attr, refToken?.line ?? -1, refToken?.start ?? -1, refToken?.length ?? -1),
                attributes = new Dictionary<AstAttr, object>()
                {
                    [AstAttr.type] = canonicalTypeExpr,
                }
            };
        }

        private string FormatLiteralAttribute(GType type, object value)
        {
            switch(type.Category)
            {
                case GType.Kind.Bool:
                    return ((bool)value) ? "true" : "false";
                case GType.Kind.Byte:
                    return ((byte)value).ToString();
                case GType.Kind.Char:
                    return EncodeCharLiteral((char)value);
                case GType.Kind.Int:
                    return ((int)value).ToString();
                case GType.Kind.UInt:
                    return ((uint)value).ToString() + "u";
                case GType.Kind.Long:
                    return ((long)value).ToString() + "L";
                case GType.Kind.ULong:
                    return ((ulong)value).ToString() + "ul";
                case GType.Kind.Float:
                    {
                        var f = (float)value;
                        if(float.IsNaN(f) || float.IsInfinity(f))
                            return null;

                        return f.ToString("R") + "f";
                    }
                case GType.Kind.Double:
                    {
                        var d = (double)value;
                        if(double.IsNaN(d) || double.IsInfinity(d))
                            return null;

                        return d.ToString("R") + "d";
                    }
                case GType.Kind.Enum:
                    return ((int)value).ToString();
                default:
                    return null;
            }
        }

        private static string TrimIntegerSuffix(string literalValue)
        {
            return literalValue?.TrimEnd('u', 'U', 'l', 'L');
        }

        private static string TrimFloatSuffix(string literalValue)
        {
            return literalValue != null && (literalValue.EndsWith("f", StringComparison.OrdinalIgnoreCase))
                ? literalValue.Substring(0, literalValue.Length - 1)
                : literalValue;
        }

        private static string TrimDoubleSuffix(string literalValue)
        {
            return literalValue != null && (literalValue.EndsWith("d", StringComparison.OrdinalIgnoreCase))
                ? literalValue.Substring(0, literalValue.Length - 1)
                : literalValue;
        }

        private char ParseCharLiteralValue(string raw)
        {
            if(string.IsNullOrEmpty(raw) || raw.Length < 3 || raw[0] != '\'' || raw[raw.Length - 1] != '\'')
                throw new SemanticException(ExceptioName.SemanticAnalysysError, null, $"invalid char literal: {raw}");

            string content = raw.Substring(1, raw.Length - 2);
            if(content.Length == 1)
                return content[0];

            if(content.Length == 2 && content[0] == '\\')
            {
                return content[1] switch
                {
                    '0' => '\0',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '\\' => '\\',
                    '\'' => '\'',
                    '"' => '"',
                    _ => content[1],
                };
            }

            throw new SemanticException(ExceptioName.SemanticAnalysysError, null, $"invalid char literal: {raw}");
        }

        private static string EncodeCharLiteral(char ch)
        {
            return ch switch
            {
                '\0' => "'\\0'",
                '\n' => "'\\n'",
                '\r' => "'\\r'",
                '\t' => "'\\t'",
                '\\' => "'\\\\'",
                '\'' => "'\\\''",
                '"' => "'\\\"'",
                _ => $"'{ch}'",
            };
        }
    }
}
