using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;

class Program {
    static Dictionary<short, OpCode> opcodes = new Dictionary<short, OpCode>();

    static void Main() {
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)) {
            if (field.FieldType == typeof(OpCode)) {
                OpCode op = (OpCode)field.GetValue(null);
                opcodes[op.Value] = op;
            }
        }

        Assembly a = Assembly.LoadFrom(@"C:\Program Files (x86)\Steam\steamapps\common\Lethal Company\Lethal Company_Data\Managed\Assembly-CSharp.dll");
        Type t = a.GetType("HangarShipDoor");
        if (t == null) {
            Console.WriteLine("HangarShipDoor not found!");
            return;
        }

        DumpMethod(t, "Update");
        DumpMethod(t, "SetDoorOpen");
        DumpMethod(t, "SetDoorClosed");
        DumpMethod(t, "PlayDoorAnimation");
        DumpMethod(t, "SetDoorButtonsEnabled");
    }

    static void DumpMethod(Type t, string methodName) {
        MethodInfo m = t.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (m == null) {
            Console.WriteLine($"--- Method {methodName} not found ---");
            return;
        }

        Console.WriteLine($"\n================ METHOD: {methodName} ================");
        MethodBody body = m.GetMethodBody();
        if (body == null) {
            Console.WriteLine("No method body.");
            return;
        }

        byte[] il = body.GetILAsByteArray();
        Module module = t.Module;
        int i = 0;
        while (i < il.Length) {
            int offset = i;
            byte opByte = il[i++];
            short opValue = opByte;
            if (opByte == 0xFE) {
                opValue = (short)((0xFE << 8) | il[i++]);
            }

            if (!opcodes.TryGetValue(opValue, out OpCode op)) {
                Console.WriteLine($"{offset:X4}: Unknown opcode {opValue:X2}");
                continue;
            }

            string operandStr = "";
            switch (op.OperandType) {
                case OperandType.InlineBrTarget:
                    int brTarget = BitConverter.ToInt32(il, i) + i + 4;
                    operandStr = $"Branch to {brTarget:X4}";
                    i += 4;
                    break;
                case OperandType.ShortInlineBrTarget:
                    sbyte sbrTarget = (sbyte)il[i];
                    operandStr = $"Branch to {(offset + 2 + sbrTarget):X4}";
                    i += 1;
                    break;
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    int token = BitConverter.ToInt32(il, i);
                    i += 4;
                    try {
                        var member = module.ResolveMember(token);
                        operandStr = $"{member.DeclaringType.Name}.{member.Name} ({member.MemberType})";
                    } catch {
                        operandStr = $"Token: {token:X8}";
                    }
                    break;
                case OperandType.InlineString:
                    int strToken = BitConverter.ToInt32(il, i);
                    i += 4;
                    try {
                        operandStr = $"\"{module.ResolveString(strToken)}\"";
                    } catch {
                        operandStr = $"StringToken: {strToken:X8}";
                    }
                    break;
                case OperandType.InlineI:
                    operandStr = BitConverter.ToInt32(il, i).ToString();
                    i += 4;
                    break;
                case OperandType.InlineR:
                    operandStr = BitConverter.ToDouble(il, i).ToString();
                    i += 8;
                    break;
                case OperandType.ShortInlineI:
                    operandStr = il[i].ToString();
                    i += 1;
                    break;
                case OperandType.ShortInlineR:
                    operandStr = BitConverter.ToSingle(il, i).ToString();
                    i += 4;
                    break;
                case OperandType.InlineVar:
                    operandStr = BitConverter.ToInt16(il, i).ToString();
                    i += 2;
                    break;
                case OperandType.ShortInlineVar:
                    operandStr = il[i].ToString();
                    i += 1;
                    break;
                // None requires no reading
            }

            Console.WriteLine($"{offset:X4}: {op.Name} {operandStr}");
        }
    }
}
