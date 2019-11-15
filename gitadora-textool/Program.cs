using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;

namespace gitadora_textool
{
    public struct RectInfo
    {
        public string ExternalFilename;
        public string Filename;
        public ushort X;
        public ushort Y;
        public ushort W;
        public ushort H;
    }

    public class Program
    {
        static int ReadInt32(BinaryReader reader, bool endianness = false)
        {
            var data = reader.ReadBytes(4);
            if (!endianness)
                Array.Reverse(data);
            return BitConverter.ToInt32(data, 0);
        }

        static int ReadInt16(BinaryReader reader, bool endianness = false)
        {
            var data = reader.ReadBytes(2);
            if (!endianness)
                Array.Reverse(data);
            return BitConverter.ToInt16(data, 0);
        }

        static void WriteInt32(BinaryWriter writer, uint input, bool endianness = false)
        {
            var data = BitConverter.GetBytes(input);
            if (!endianness)
                Array.Reverse(data);
            writer.Write(data);
        }

        static void WriteInt16(BinaryWriter writer, ushort input, bool endianness = false)
        {
            var data = BitConverter.GetBytes(input);
            if (!endianness)
                Array.Reverse(data);
            writer.Write(data);
        }


        public static byte[] ExtractImageCore(BinaryReader reader, RectInfo? rectInfoList = null)
        {

            var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var endianCheck1 = reader.ReadInt32();
            var endianCheck2 = reader.ReadInt32();
            var requiresEndianFix = endianCheck2 == 0x00010100;
            
            reader.BaseStream.Seek(0x0c, SeekOrigin.Begin);

            var dataSize = ReadInt32(reader, requiresEndianFix) - 0x40;
            var width = ReadInt16(reader, requiresEndianFix);
            var height = ReadInt16(reader, requiresEndianFix);

            if (!requiresEndianFix)
                reader.BaseStream.Seek(0x03, SeekOrigin.Current);


            /*
                GRAYSCALE_FORMAT 0x01
                GRAYSCALE_FORMAT_2 0x06
                BGR_16BIT_FORMAT 0x0C
                BGRA_16BIT_FORMAT 0x0D
                BGR_FORMAT 0x0E
                BGRA_FORMAT 0x10
                BGR_4BIT_FORMAT 0x11
                BGR_8BIT_FORMAT 0x12
                DXT1_FORMAT 0x16
                DXT3_FORMAT 0x18
                DXT5_FORMAT 0x1A
             */

            var dataFormat = reader.ReadByte();

            reader.BaseStream.Seek(0x40, SeekOrigin.Begin);
            var bitmapData = reader.ReadBytes(dataSize);

            var paletteEntries = new List<Color>();

            var pixelFormat = PixelFormat.Undefined;
            if (dataFormat == 0x01)
            {
                // GRAYSCALE_FORMAT8
                pixelFormat = PixelFormat.Format8bppIndexed;
            }
            else if (dataFormat == 0x06)
            {
                // GRAYSCALE_FORMAT_2
                pixelFormat = PixelFormat.Format8bppIndexed;
            }
            else if (dataFormat == 0x0c)
            {
                // BGR_16BIT_FORMAT
                pixelFormat = PixelFormat.Format16bppRgb565;
            }
            else if (dataFormat == 0x0d)
            {
                // BGRA_16BIT_FORMAT
                byte[] newBitmapData = new byte[width * height * 4];
                for (int didx = 0, i = 0; i < height; i++)
                {
                    for (var j = 0; j < width; j++, didx += 2)
                    {
                        ushort c = (ushort)((bitmapData[didx + 1] << 8) | bitmapData[didx]);

                        DxtUtil.ConvertArgb4444ToArgb8888(c, out var a, out var r, out var g, out var b);

                        newBitmapData[(j * 4) + (i * width * 4)] = a;
                        newBitmapData[(j * 4) + 1 + (i * width * 4)] = r;
                        newBitmapData[(j * 4) + 2 + (i * width * 4)] = g;
                        newBitmapData[(j * 4) + 3 + (i * width * 4)] = b;
                    }
                }
                
                bitmapData = newBitmapData;

                pixelFormat = PixelFormat.Format32bppArgb;
            }
            else if (dataFormat == 0x0e)
            {
                // BGR_FORMAT
                pixelFormat = PixelFormat.Format24bppRgb;
            }
            else if (dataFormat == 0x10)
            {
                // BGRA_FORMAT
                pixelFormat = PixelFormat.Format32bppArgb;
            }
            else if (dataFormat == 0x11)
            {
                // BGR_4BIT_FORMAT
                var bitmapDataOnly = new byte[width * height / 2];
                Buffer.BlockCopy(bitmapData, 0, bitmapDataOnly, 0, bitmapDataOnly.Length);

                var paletteData = new byte[bitmapData.Length - bitmapDataOnly.Length - 0x14]; // Skip palette header
                Buffer.BlockCopy(bitmapData, bitmapDataOnly.Length + 0x14, paletteData, 0, paletteData.Length);

                bitmapData = bitmapDataOnly;

                pixelFormat = PixelFormat.Format4bppIndexed;

                for (int i = 0; i < paletteData.Length / 4; i++)
                {
                    paletteEntries.Add(Color.FromArgb(paletteData[i + 3], paletteData[i], paletteData[i + 1], paletteData[i + 2]));
                }
            }
            else if (dataFormat == 0x12)
            {
                // BGR_8BIT_FORMAT
                var bitmapDataOnly = new byte[width * height];
                Buffer.BlockCopy(bitmapData, 0, bitmapDataOnly, 0, bitmapDataOnly.Length);

                var paletteData = new byte[bitmapData.Length - bitmapDataOnly.Length - 0x14]; // Skip palette header
                Buffer.BlockCopy(bitmapData, bitmapDataOnly.Length + 0x14, paletteData, 0, paletteData.Length);

                bitmapData = bitmapDataOnly;

                pixelFormat = PixelFormat.Format8bppIndexed;

                for (int i = 0; i < paletteData.Length / 4; i++)
                {
                    paletteEntries.Add(Color.FromArgb(paletteData[i + 3], paletteData[i], paletteData[i + 1], paletteData[i + 2]));
                }
            }
            else if (dataFormat == 0x16)
            {
                // DXT1_FORMAT
                pixelFormat = PixelFormat.Format32bppArgb;
                bitmapData = DxtUtil.DecompressDxt1(bitmapData, width, height);
            }
            else if (dataFormat == 0x18)
            {
                // DXT3_FORMAT
                pixelFormat = PixelFormat.Format32bppArgb;
                bitmapData = DxtUtil.DecompressDxt3(bitmapData, width, height);
            }
            else if (dataFormat == 0x1a)
            {
                // DXT5_FORMAT
                pixelFormat = PixelFormat.Format32bppArgb;
                bitmapData = DxtUtil.DecompressDxt5(bitmapData, width, height);
            }
            else
            {
                throw new Exception(String.Format("Found unknown pixel format: {0:x2}", dataFormat));
            }
            
            for (int i = 0; i < bitmapData.Length;)
            {
                if (pixelFormat == PixelFormat.Format16bppArgb1555)
                {
                    var a = bitmapData[i] & 0x0f;
                    var r = (bitmapData[i] >> 4) & 0x0f;
                    var g = bitmapData[i + 1] & 0x0f;
                    var b = (bitmapData[i + 1] >> 4) & 0x0f;

                    bitmapData[i + 1] = (byte)((a << 4) | b);
                    bitmapData[i + 0] = (byte)((g << 4) | r);

                    i += 2;
                }
                else if (pixelFormat == PixelFormat.Format16bppRgb565)
                {
                    bitmapData[i] = (byte)((bitmapData[i] & 0xc0) | (bitmapData[i] & 0x3f) >> 1);
                    i += 2;
                }
                else if (pixelFormat == PixelFormat.Format24bppRgb)
                {
                    var t = bitmapData[i + 2];
                    bitmapData[i + 2] = bitmapData[i];
                    bitmapData[i] = t;
                    i += 3;
                }
                else if (pixelFormat == PixelFormat.Format32bppArgb)
                {
                    var t = bitmapData[i + 2];
                    bitmapData[i + 2] = bitmapData[i];
                    bitmapData[i] = t;
                    i += 4;
                }
                else
                {
                    break;
                }
            }

            if (pixelFormat == PixelFormat.Undefined
                || pixelFormat == PixelFormat.Format16bppArgb1555)
            {
                // Create DDS file
                var output = new List<byte>();

                output.AddRange(new byte[]
                {
                    0x44, 0x44, 0x53, 0x20, 0x7C, 0x00, 0x00, 0x00, 0x07, 0x10, 0x08, 0x00
                });

                output.AddRange(BitConverter.GetBytes(height));
                output.AddRange(BitConverter.GetBytes(width));
                output.AddRange(BitConverter.GetBytes(dataSize));

                output.AddRange(new byte[]
                {
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00
                });

                if (pixelFormat == PixelFormat.Format16bppArgb1555)
                {
                    output.AddRange(new byte[]
                    {
                        0x41, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x0F, 0x00, 0x00,
                        0xF0, 0x00, 0x00, 0x00, 0x0F, 0x00, 0x00, 0x00, 0x00, 0xF0, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00,
                        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
                    });
                }
                else if (pixelFormat == PixelFormat.Format16bppRgb555)
                {
                    output.AddRange(new byte[]
                    {
                        0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0xF8, 0x00, 0x00,
                        0xE0, 0x07, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00,
                        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
                    });
                }
                else
                {
                    output.AddRange(new byte[]
                    {
                        0x04, 0x00, 0x00, 0x00
                    });

                    if (dataFormat == 0x16)
                    {
                        output.AddRange(Encoding.ASCII.GetBytes("DXT1"));
                    }
                    else if (dataFormat == 0x18)
                    {
                        output.AddRange(Encoding.ASCII.GetBytes("DXT3"));
                    }
                    else if (dataFormat == 0x1a)
                    {
                        output.AddRange(Encoding.ASCII.GetBytes("DXT5"));
                    }

                    output.AddRange(new byte[]
                    {
                        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                        0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
                    });
                }

                output.AddRange(bitmapData);

                return output.ToArray();
            }
            else
            {
                var b = new Bitmap(width, height, pixelFormat);

                if (pixelFormat == PixelFormat.Format8bppIndexed)
                {
                    ColorPalette palette = b.Palette;
                    Color[] entries = palette.Entries;

                    if (paletteEntries.Count == 0)
                    {
                        for (int i = 0; i < 256; i++)
                        {
                            Color c = Color.FromArgb((byte)i, (byte)i, (byte)i);
                            entries[i] = c;
                        }
                    } else
                    {
                        for (int i = 0; i < paletteEntries.Count; i++)
                        {
                            entries[i] = paletteEntries[i];
                        }
                    }

                    b.Palette = palette;
                }

                var boundsRect = new Rectangle(0, 0, width, height);
                BitmapData bmpData = b.LockBits(boundsRect,
                    ImageLockMode.WriteOnly,
                    b.PixelFormat);

                IntPtr ptr = bmpData.Scan0;
                
                if (pixelFormat != PixelFormat.Format24bppRgb)
                {
                    int bytes = bmpData.Stride * b.Height;
                    Marshal.Copy(bitmapData, 0, ptr, bytes);
                }
                else
                {
                    // Because things are stupid, we have to pad the lines for 24bit images ourself...
                    for (int i = 0; i < height; i++)
                    {
                        Marshal.Copy(bitmapData, i * width * 3, ptr + (bmpData.Stride * i), width * 3);
                    }
                }

                b.UnlockBits(bmpData);

                // Split into separate smaller bitmap
                if (rectInfoList != null)
                {
                    var rect = new Rectangle(rectInfoList.Value.X, rectInfoList.Value.Y, rectInfoList.Value.W, rectInfoList.Value.H);
                    Bitmap subimage = new Bitmap(rect.Width, rect.Height);

                    Console.WriteLine(rect);

                    using (Graphics g = Graphics.FromImage(subimage))
                    {
                        g.DrawImage(b, new Rectangle(0, 0, subimage.Width, subimage.Height), rect, GraphicsUnit.Pixel);
                    }

                    b = subimage;
                }
                
                ImageConverter converter = new ImageConverter();
                return (byte[])converter.ConvertTo(b, typeof(byte[]));
            }
        }

        static void ExtractImage(string filename)
        {
            using (BinaryReader reader = new BinaryReader(File.Open(filename, FileMode.Open)))
            {
                var data = ExtractImageCore(reader);

                var ext = "png";
                if (data[0] == 'D' && data[1] == 'D' && data[2] == 'S' && data[3] == ' ')
                {
                    ext = "dds";
                }

                string outputFilename = String.Format("{0}.{1}", Path.Combine(Path.GetDirectoryName(filename), Path.GetFileNameWithoutExtension(filename)), ext);
                Console.WriteLine(outputFilename);
                File.WriteAllBytes(outputFilename, data);
            }
        }

        public static byte[] CreateImageCore(byte[] data, bool requiresEndianFix = false)
        {
            var image = Bitmap.FromStream(new MemoryStream(data));
            var bmpOrig = new Bitmap(image);

            var pixelFormat = image.PixelFormat;
            
            if (pixelFormat == PixelFormat.Format8bppIndexed)
            {
                pixelFormat = PixelFormat.Format32bppArgb;
            }

            var bmp = new Bitmap(bmpOrig.Width, bmpOrig.Height, pixelFormat);
            using (Graphics gr = Graphics.FromImage(bmp))
            {
                gr.DrawImage(bmpOrig, new Rectangle(0, 0, bmp.Width, bmp.Height));
            }

            var bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly,
                pixelFormat);

            IntPtr ptr = bmpData.Scan0;
            int bytes = Math.Abs(bmpData.Stride) * bmp.Height;
            byte[] rawData = new byte[bytes];

            Marshal.Copy(ptr, rawData, 0, bytes);

            bmp.UnlockBits(bmpData);

            var stream = new MemoryStream();
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                Func<byte[], bool> writeBytes = delegate (byte[] inputData)
                {
                    writer.Write(requiresEndianFix ? inputData.Reverse().ToArray() : inputData);
                    return true;
                };

                writeBytes(new byte[] { 0x54, 0x58, 0x44, 0x54 });
                if (requiresEndianFix)
                {
                    writeBytes(new byte[] { 0x00, 0x01, 0x00, 0x00 });
                    writeBytes(new byte[] { 0x00, 0x01, 0x01, 0x00 });
                }
                else
                {
                    writeBytes(new byte[] { 0x00, 0x01, 0x02, 0x00 });
                    writeBytes(new byte[] { 0x00, 0x01, 0x02, 0x00 });
                }

                WriteInt32(writer, (uint)(rawData.Length + 0x40), requiresEndianFix);
                WriteInt16(writer, (ushort)(bmp.Width), requiresEndianFix);
                WriteInt16(writer, (ushort)(bmp.Height), requiresEndianFix);

                if (bmp.PixelFormat == PixelFormat.Format24bppRgb)
                {
                    if (requiresEndianFix)
                    {
                        writeBytes(new byte[] { 0x11, 0x22, 0x10, 0x0E });
                    }
                    else
                    {
                        writeBytes(new byte[] { 0x11, 0x11, 0x10, 0x0E });
                    }
                }
                else if (bmp.PixelFormat == PixelFormat.Format32bppArgb)
                {
                    if (requiresEndianFix)
                    {
                        writeBytes(new byte[] { 0x11, 0x22, 0x10, 0x10 });
                    }
                    else
                    {
                        writeBytes(new byte[] { 0x11, 0x11, 0x10, 0x10 });
                    }
                }
                else
                {
                    Console.WriteLine("Expected 24bit or 32bit image. Don't know how to handle pixel format {0}", bmp.PixelFormat);
                    Environment.Exit(1);
                }

                for (int i = 0; i < 0x14; i++)
                {
                    writer.Write((byte)0x00);
                }

                if (bmp.PixelFormat == PixelFormat.Format24bppRgb)
                {
                    WriteInt32(writer, 0x01, requiresEndianFix);
                }
                else if (bmp.PixelFormat == PixelFormat.Format32bppArgb)
                {
                    WriteInt32(writer, 0x03, requiresEndianFix);
                }

                for (int i = 0; i < 0x10; i++)
                {
                    writer.Write((byte)0x00);
                }

                var bpp = bmp.PixelFormat == PixelFormat.Format32bppArgb ? 4 : 3;
                for (int i = 0; i < rawData.Length; i += bpp)
                {
                    var t = rawData[i];
                    rawData[i] = rawData[i + 2];
                    rawData[i + 2] = t;
                }

                writer.Write(rawData);
            }

            var outputData = stream.GetBuffer();
            Array.Resize(ref outputData, bytes + 0x40);
            return outputData;
        }

        static void CreateImage(string filename)
        {
            string outputFilename = String.Format("{0}.tex", Path.Combine(Path.GetDirectoryName(filename), Path.GetFileNameWithoutExtension(filename)));
            Console.WriteLine(outputFilename);

            var rawData = File.ReadAllBytes(filename);
            var data = CreateImageCore(rawData, false);
            File.WriteAllBytes(outputFilename, data);
        }

        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("usage: {0} input_filename", AppDomain.CurrentDomain.FriendlyName);
                return;
            }

            foreach (var filename in args)
            {
                var isExtract = false;
                using (BinaryReader reader = new BinaryReader(File.Open(filename, FileMode.Open)))
                {
                    var magic = reader.ReadBytes(4);
                    if (magic.SequenceEqual(new byte[] { 0x54, 0x58, 0x44, 0x54 })
                        || magic.SequenceEqual(new byte[] { 0x54, 0x44, 0x58, 0x54 }))
                    {
                        isExtract = true;
                    }
                }

                try
                {
                    if (isExtract)
                    {
                        ExtractImage(filename);
                    }
                    else
                    {
                        CreateImage(filename);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error occurred: {0}", e.Message);
                    Environment.Exit(1);
                }
            }
        }
    }
}
