using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using static LenovoCustomLogo.Util;

namespace LenovoCustomLogo
{
    internal class Program
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern unsafe uint GetFirmwareEnvironmentVariableExW(string lpName, string lpGuid, void* pBuffer, int nSize, ref int attr);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern unsafe bool SetFirmwareEnvironmentVariableExW(string lpName, string lpGuid, void* pValue, int nSize, int attributes);

        [DllImport("libc", SetLastError = true)]
        internal static extern uint geteuid();

        [Flags]
        public enum LogoFormat : byte
        {
            JPG = 1,
            BMP = 0x10,
            PNG = 0x20,
        }

        private static string LogoFormatToString(LogoFormat value) {
            return string.Join(", ", Enum.GetValues<LogoFormat>().Where(x => value.HasFlag(x)).Select(x => x.ToString()));
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct LogoInfo
        {
            public byte Enabled;
            public int Width;
            public int Height;
            public LogoFormat Format;
        }

        public static unsafe LogoInfo ReadLogoInfo()
        {
            var logoinfo = new LogoInfo();
            int attr = 7;
            string name = "LBLDESP";
            string guid = "871455D0-5576-4FB8-9865-AF0824463B9E";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var size = GetFirmwareEnvironmentVariableExW(name, "{" + guid + "}", &logoinfo, sizeof(LogoInfo), ref attr);

                ThrowForLastWin32Error();
                if (size != sizeof(LogoInfo))
                {
                    throw new InvalidOperationException("failed to read LogoInfo");
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                const int EfiVarAttrLength = 4;
                using var fs = File.OpenRead(Path.Combine("/sys/firmware/efi/efivars/", name + "-" + guid));
                var buf = new byte[sizeof(LogoInfo) + EfiVarAttrLength];
                var size = fs.Read(buf, 0, sizeof(LogoInfo) + EfiVarAttrLength);
                if (size != sizeof(LogoInfo) + EfiVarAttrLength)
                {
                    throw new InvalidOperationException("failed to read LogoInfo");
                }
                logoinfo.Enabled = buf[4];
                logoinfo.Width = BitConverter.ToInt32(buf[5..9], 0);
                logoinfo.Height = BitConverter.ToInt32(buf[9..13], 0);
                logoinfo.Format = (LogoFormat) buf[13];
            }
            return logoinfo;
        }

        public static unsafe void WriteLogoInfo(in LogoInfo logoInfo)
        {
            fixed (void* ptr = &logoInfo)
            {
                int attr = 7;
                string name = "LBLDESP";
                string guid = "871455D0-5576-4FB8-9865-AF0824463B9E";
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    SetFirmwareEnvironmentVariableExW(name, "{" + guid + "}", ptr, sizeof(LogoInfo), attr);
                    ThrowForLastWin32Error();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    const int EfiVarAttrLength = 4;
                    using var fs = File.OpenWrite(Path.Combine("/sys/firmware/efi/efivars/", name + "-" + guid));
                    {
                        using (var w = new BinaryWriter(fs))
                        {
                            w.Write(attr);
                            w.Write(logoInfo.Enabled);
                            w.Write(logoInfo.Width);
                            w.Write(logoInfo.Height);
                            w.Write((byte) logoInfo.Format);
                        }
                    }
                }
            }
        }

        public static unsafe byte[] ReadLogoVCM()
        {
            var logovcm = new byte[40];
            int attr = 7;
            uint size = 0;
            string name = "LBLDVC";
            string guid = "871455D1-5576-4FB8-9865-AF0824463C9F";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                fixed (byte* ptr = logovcm)
                    size = GetFirmwareEnvironmentVariableExW(name, "{" + guid + "}", ptr, 40, ref attr);
                ThrowForLastWin32Error();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                const int EfiVarAttrLength = 4;
                using var fs = File.OpenRead(Path.Combine("/sys/firmware/efi/efivars/", name + "-" + guid));
                var buf = new byte[40 + EfiVarAttrLength];
                size = (uint) fs.Read(buf, 0, 40 + EfiVarAttrLength);
                size -= EfiVarAttrLength;
                logovcm = buf[4..];
            }
            if (size != 40)
            {
                throw new InvalidOperationException("failed to read LogoVCM");
            }
            return logovcm;
        }

        public static unsafe void WriteLogoVCM(ReadOnlySpan<byte> vcm)
        {
            int  attr = 7;
            string name = "LBLDVC";
            string guid = "871455D1-5576-4FB8-9865-AF0824463C9F";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                fixed (byte* ptr = vcm)
                    SetFirmwareEnvironmentVariableExW(name, "{" + guid + "}", ptr, 40, attr);
                ThrowForLastWin32Error();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                const int EfiVarAttrLength = 4;
                using var fs = File.OpenWrite(Path.Combine("/sys/firmware/efi/efivars/", name + "-" + guid));

                fs.WriteByte((byte) attr);
                fs.WriteByte((byte) 0x00);
                fs.WriteByte((byte) 0x00);
                fs.WriteByte((byte) 0x00);
                fs.Write(vcm);
            }
        }

        public static uint ReadLogoCrc()
        {
            var logovcm = ReadLogoVCM();
            var crc = Unsafe.ReadUnaligned<uint>(ref logovcm[4]);
            return crc;
        }

        public static unsafe void WriteLogoCrc(uint crc)
        {
            var logovcm = ReadLogoVCM();
            Unsafe.WriteUnaligned(ref logovcm[4], crc);
            WriteLogoVCM(logovcm);
        }

        static uint GetLogoFileCrc(string path)
        {
            using var fs = File.OpenRead(path);
            var buf = new byte[512];
            var len = fs.Read(buf, 0, 512);
            return Crc32.Forward(buf.AsSpan().Slice(0, len));
        }

        static void PrintStatus(in LogoInfo info, uint logocrc)
        {
            Console.WriteLine("Custom logo status: ");
            Console.WriteLine("  Enabled: \t{0}", info.Enabled != 0);
            Console.WriteLine("  Width: \t{0}", info.Width);
            Console.WriteLine("  Height: \t{0}", info.Height);
            Console.WriteLine("  Formats: \t{0}", info.Format);

            if (info.Enabled != 0 && efivol != null)
            {
                var logofiles = new List<string>();
                var prefix = Path.Combine(efivol, "EFI", "Lenovo", "Logo", $"mylogo_{info.Width}x{info.Height}");
                if (info.Format.HasFlag(LogoFormat.JPG))
                {
                    logofiles.Add(prefix + ".jpg");
                }
                if (info.Format.HasFlag(LogoFormat.BMP))
                {
                    logofiles.Add(prefix + ".bmp");
                }
                if (info.Format.HasFlag(LogoFormat.PNG))
                {
                    logofiles.Add(prefix + ".png");
                }

                foreach (var logofile in logofiles)
                {
                    try
                    {
                        var filecrc = GetLogoFileCrc(logofile);
                        if (filecrc == logocrc)
                        {
                            Console.WriteLine("  Current: \t{0}", logofile);
                            break;
                        }
                    }
                    catch (IOException) { }
                }
            }
        }

        static string? efivol;

        static bool promoted = false;

        static void PromotePrivileges()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (geteuid() != 0)
                {
                    Console.Error.WriteLine("Warning: Running without root; trying to change anything will likely fail!");
                }
                return;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (promoted) return;
                PromoteProcessPrivileges(SE_SYSTEM_ENVIRONMENT_NAME, true);
                PromoteProcessPrivileges(SE_BACKUP_NAME, true);
                promoted = true;
            }
        }

        static void PrintUsage()
        {
            var argv0 = GetArgv0();
            Console.WriteLine("Usage:");
            Console.WriteLine($"  {argv0} status");
            Console.WriteLine($"  {argv0} set <image file>");
            Console.WriteLine($"  {argv0} reset");
        }

        static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var e = args.ExceptionObject as Exception;
                if (e != null)
                {
                    Console.Error.WriteLine(e);
                }
                if (args.IsTerminating)
                {
                    Environment.Exit(255);
                }
            };

            efivol = VolMgr.GetEfiSystemPartitionVolumePath();

            string? command = null;
            if (args.Length >= 1)
            {
                command = args[0].ToLowerInvariant();
            }
            if (command == null || command == "status")
            {
                PromotePrivileges();
                var logoinfo = ReadLogoInfo();
                var logocrc = ReadLogoCrc();
                PrintStatus(logoinfo, logocrc);
            }
            else if (command == "help")
            {
                PrintUsage();
                return 0;
            }
            else if (command == "set")
            {
                if (args.Length != 2)
                {
                    PrintUsage();
                    return 1;
                }
                var filename = args[1]!;

                if (efivol == null)
                {
                    throw new ApplicationException("cannot resolve EFI system partition");
                }

                PromotePrivileges();
                var info = ReadLogoInfo();
                var logodir = Path.Combine(efivol, "EFI", "Lenovo", "Logo");
                var prefix = Path.Combine(logodir, $"mylogo_{info.Width}x{info.Height}");
                string logofile;
                //TODO: restore
                /*using var bmp = Image.FromFile(filename);
                bool needConvert = false;
                if (info.Format.HasFlag(LogoFormat.BMP) && bmp.RawFormat.Equals(ImageFormat.Bmp))
                    logofile = prefix + ".bmp";
                else if (info.Format.HasFlag(LogoFormat.JPG) && bmp.RawFormat.Equals(ImageFormat.Jpeg))
                    logofile = prefix + ".jpg";
                else if (info.Format.HasFlag(LogoFormat.PNG) && bmp.RawFormat.Equals(ImageFormat.Png))
                    logofile = prefix + ".png";
                else if (info.Format.HasFlag(LogoFormat.BMP))
                {
                    logofile = prefix + ".bmp";
                    needConvert = true;
                }
                else
                    throw new NotSupportedException("no supported image format");

                if (bmp.Width > info.Width || bmp.Height > info.Height)
                    throw new ApplicationException("image is too large");
*/
                bool needConvert = false;
                logofile = prefix + ".bmp";
                try
                {
                    Console.WriteLine("Writing {0}", logofile);
                    Directory.CreateDirectory(logodir);
                    if (needConvert)
                    {
                        using var fs = File.OpenWrite(logofile);
                        //bmp.Save(fs, ImageFormat.Bmp);
                    }
                    else
                    {
                        File.Copy(filename, logofile, true);
                    }
                }
                catch (IOException e)
                {
                    try
                    {
                        File.Delete(logofile);
                    }
                    catch { }
                    throw e;
                }
                info.Enabled = 1;
                var crc = GetLogoFileCrc(logofile);
                Console.WriteLine("Setting EFI variables");
                WriteLogoInfo(info);
                WriteLogoCrc(crc);
            }
            else if (command == "reset")
            {
                if (args.Length != 1)
                {
                    PrintUsage();
                    return 1;
                }
                PromotePrivileges();
                Console.WriteLine("Setting EFI variables");
                var info = ReadLogoInfo();
                info.Enabled = 0;
                WriteLogoInfo(info);
            }
            else
            {
                PrintUsage();
            }

            return 0;
        }
    }
}