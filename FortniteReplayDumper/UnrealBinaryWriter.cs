using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FortniteReplayDumper
{
    public class UnrealBinaryWriter : BinaryWriter
    {
        public UnrealBinaryWriter(Stream output) : base(output) { }

        public void WriteString(string str)
        {
            byte[] bytes = StringToByteArray(str);

            Write(bytes);
        }

        public void Write(string value, bool unicode = false)
        {
            Write(value, value.Length, unicode);
        }

        public void Write(string value, int fixedSize, bool unicode)
        {
            if(fixedSize < value.Length)
            {
                throw new InvalidOperationException("Invalid fixed size length");
            }

            int length = fixedSize + 1;

            if(value.Length != fixedSize)
            {
                value += new string(' ', fixedSize - value.Length);
            }

            value += '\0';


            byte[] data;

            if (unicode)
            {
                length *= -1;

                data = Encoding.Unicode.GetBytes(value);
            }
            else
            {
                data = Encoding.ASCII.GetBytes(value);
            }

            Write(length);
            Write(data);
        }

        public void WriteArray<T>(T[] arr, Action<T> func1)
        {
            Write(arr.Length);

            foreach(T item in arr)
            {
                func1(item);
            }
        }

        //https://stackoverflow.com/questions/311165/how-do-you-convert-a-byte-array-to-a-hexadecimal-string-and-vice-versa
        private static byte[] StringToByteArray(string hexString)
        {
            int numberChars = hexString.Length;
            byte[] bytes = new byte[numberChars / 2];

            for (int i = 0; i < numberChars; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hexString.Substring(i, 2), 16);
            }

            return bytes;
        }
    }
}
