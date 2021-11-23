using System;
using System.IO;
using SevenZip.Compression.LZMA;

public class ByteCompress
{
    public static byte[] LzmaCompress(byte[] inpbuf)
    {
        var enc = new Encoder();

        MemoryStream msInp = new MemoryStream(inpbuf);
        MemoryStream msOut = new MemoryStream();
        enc.WriteCoderProperties(msOut);
        enc.Code(msInp, msOut, -1, -1, null);
        return msOut.ToArray();
    }

    public static byte[] LzmaDecompress(byte[] inpbuf)
    {
        Decoder coder = new Decoder();
        MemoryStream input = new MemoryStream(inpbuf);
        byte[] properties = new byte[5];
        input.Read(properties, 0, 5);

        byte[] fileLengthBytes = new byte[8];
        input.Read(fileLengthBytes, 0, 8);
        long fileLength = BitConverter.ToInt64(fileLengthBytes, 0);
        coder.SetDecoderProperties(properties, (uint) fileLength);

        MemoryStream output = new MemoryStream();

        coder.Code(input, output, input.Length, fileLength, null);
        return output.ToArray();
    }

    public static byte[] ZIPDecompress(byte[] inpbuf)
    {
        byte[] output = ZIPDecompress(inpbuf);

        return output;
    }

    public static void CopyStream(Stream input, Stream output)
    {
        byte[] buffer = new byte[2000];
        int len;
        while ((len = input.Read(buffer, 0, 2000)) > 0)
        {
            output.Write(buffer, 0, len);
        }

        output.Flush();
    }
}