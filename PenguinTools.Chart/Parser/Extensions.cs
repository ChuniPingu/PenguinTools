using System.Globalization;
using System.Text;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Chart.Parser;

internal static class Extensions
{
    public static decimal ToDecimal(this object value, long position, string path, string field, IDiagnosticSink diagnostics)
    {
        try
        {
            var kind = field.Trim() switch
            {
                "bpm" or "mbpm" => NativeNumberKind.Bpm,
                "smod" or "til" => NativeNumberKind.Speed,
                _ => NativeNumberKind.Value
            };
            var rendered = Convert.ToString(value, CultureInfo.InvariantCulture);
            var adjusted = false;
            var result = value switch
            {
                double d => NativeNumber.Read(d, kind, out adjusted),
                float f => NativeNumber.Read(f, kind, out adjusted, shortest: f.ToString("R", CultureInfo.InvariantCulture)),
                _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture)
            };
            if (adjusted)
            {
                diagnostics.Report(new LocationDiagnostic(Severity.Warning,
                    Msg.Create(MsgKeys.Warn_Number_adjusted, $"MGXC {field.Trim()}", rendered,
                        result.ToString("G29", CultureInfo.InvariantCulture)), checked((int)position), path));
            }
            return result;
        }
        catch (Exception ex) when (ex is OverflowException or FormatException or InvalidCastException)
        {
            var rendered = Convert.ToString(value, CultureInfo.InvariantCulture);
            throw new LocationDiagnosticException(Msg.Create(MsgKeys.MgCrit_Invalid_number, $"MGXC {field.Trim()}", rendered),
                checked((int)position), path);
        }
    }

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
            if (size < 0 || br.BaseStream.CanSeek && size > br.BaseStream.Length - br.BaseStream.Position)
            {
                throw new DiagnosticException(Msg.Create(MsgKeys.Error_Size_Incompatible,
                    size, expected, br.BaseStream.CanSeek ? br.BaseStream.Length - br.BaseStream.Position : 0));
            }
            var bytes = br.ReadBytes(size);
            if (bytes.Length < size)
            {
                MessageDescriptor msg = Msg.Create(MsgKeys.Error_Size_Incompatible, size, expected, bytes.Length);
                throw new DiagnosticException(msg);
            }

            using var ms = new MemoryStream(bytes);
            using var nr = new BinaryReader(ms);
            while (nr.BaseStream.Position < nr.BaseStream.Length)
            {
                action(nr);
            }
        }

        public string ReadUtf8String(int length)
        {
            if (length < 0 || br.BaseStream.CanSeek && length > br.BaseStream.Length - br.BaseStream.Position)
            {
                throw new DiagnosticException(Msg.Create(MsgKeys.Error_Size_Incompatible,
                    length, "UTF8", br.BaseStream.CanSeek ? br.BaseStream.Length - br.BaseStream.Position : 0));
            }

            var buffer = br.ReadBytes(length);
            if (buffer.Length == length)
            {
                return Encoding.UTF8.GetString(buffer);
            }

            MessageDescriptor msg = Msg.Create(MsgKeys.Error_Size_Incompatible, length, "UTF8", buffer.Length);
            throw new DiagnosticException(msg);
        }

        public object ReadField()
        {
            var type = br.ReadInt16();
            var attr = br.ReadInt16();

            return type switch
            {
                4 => br.ReadUtf8String(unchecked((ushort)attr)),
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
            if (type == 4)
            {
                return br.ReadUtf8String(attr);
            }

            MessageDescriptor msg = Msg.Create(MsgKeys.MgCrit_Unrecognized_data_type, type);
            throw new LocationDiagnosticException(msg, checked((int)br.BaseStream.Position));
        }
    }
}
