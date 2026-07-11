using System.Text;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Chart.Parser;

internal static class Extensions
{
    public static decimal Round(this object obj, int decimals = 6)
    {
        return obj switch
        {
            double d => Math.Round((decimal)d, decimals),
            int i => i,
            short s => s,
            byte b => b,
            _ => Math.Round((decimal)obj, decimals)
        };
    }

    extension(BinaryReader br)
    {
        public void ReadBlock(string expected, Action<BinaryReader> action)
        {
            var actual = br.ReadUtf8String(4);
            if (actual != expected)
            {
                MessageDescriptor msg = Msg.Create(MsgKeys.Error_Invalid_Header, actual, expected);
                throw new DiagnosticException(msg);
            }

            var size = br.ReadInt32();
            var bytes = br.ReadBytes(size);
            if (bytes.Length < size)
            {
                MessageDescriptor msg = Msg.Create(MsgKeys.Error_Size_Incompatible, size, expected, bytes.Length);
                throw new DiagnosticException(msg);
            }

            using var ms = new MemoryStream(bytes);
            using var nr = new BinaryReader(ms);
            while (nr.BaseStream.Position < nr.BaseStream.Length) action(nr);
        }

        public string ReadUtf8String(int length)
        {
            if (length > 128) return Encoding.UTF8.GetString(br.ReadBytes(length));
            Span<byte> buffer = stackalloc byte[length];
            var read = br.Read(buffer);
            if (read == length) return Encoding.UTF8.GetString(buffer);
            MessageDescriptor msg = Msg.Create(MsgKeys.Error_Size_Incompatible, length, "UTF8", read);
            throw new DiagnosticException(msg);
        }

        public object ReadField()
        {
            var type = br.ReadInt16();
            var attr = br.ReadInt16();

            return type switch
            {
                4 => br.ReadUtf8String(attr),
                3 => br.ReadDouble(),
                2 => br.ReadInt32(),
                1 or 0 => (int)attr,
                _ => throw new LocationDiagnosticException(
                    Msg.Create(MsgKeys.MgCrit_Unrecognized_data_type, type),
                    checked((int)br.BaseStream.Position))
            };
        }

        public object ReadWideField()
        {
            var type = br.ReadInt32();
            var attr = br.ReadInt32();
            if (type == 4) return br.ReadUtf8String(attr);
            MessageDescriptor msg = Msg.Create(MsgKeys.MgCrit_Unrecognized_data_type, type);
            throw new LocationDiagnosticException(msg, checked((int)br.BaseStream.Position));
        }
    }
}