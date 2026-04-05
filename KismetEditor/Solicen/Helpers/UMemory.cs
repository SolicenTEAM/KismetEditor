using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Solicen.Helpers
{
    public class UMemory
    {
        private int index = 0;
        private readonly byte[] _data;
        public int Lenght => _data.Length;
        public UMemory(byte[] array)
        {
            _data = array;
        }
        public byte ReadByte()
        {
            if (index >= _data.Length) return 0;
            var result = _data[index];
            index++;
            return result;
        }
        public int GetPosition() => index;
        public bool EndOfFile()
        {
            if (index-1 == _data.Length) return true;
            else return false;
        }

        private byte[] GetBytes(byte[] source, int index, int count)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (index < 0 || index >= source.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (count < 0 || index + count > source.Length) throw new ArgumentOutOfRangeException(nameof(count));

            byte[] result = new byte[count];
            Array.Copy(source, index, result, 0, count);
            return result;
        }

        public byte[] GetBytes(int index, int count)
        {
            byte[] result = new byte[count];
            Array.Copy(this._data, index, result, 0, count);
            return result;
        }

        public void SetPosition(int position)
        {
            index = position;
        }

        public byte[] GetByteString(Encoding encoding, bool savePosition = true, byte lB = 0x00)
        {
            int position = index;
            var result = new List<byte>();
            if (encoding == Encoding.Unicode)
            {
                while (true)
                {
                    result.Add(ReadByte());
                    result.Add(ReadByte());
                    if (result[result.Count - 1] == lB &&
                        result[result.Count - 2] == lB)
                        break;
                }
            }
            else
            {
                while (true)
                {
                    result.Add(ReadByte());
                    if (result.Count > 0 && result[result.Count - 1] == lB)
                        break;
                }
            }

            if (savePosition) index = position;
            return result.ToArray();
        }

        public int GetInt8(int count = 4)
        {
            var result = new List<byte>();
            for (int i = 0; i < count; i++)
            {
                result.Add(ReadByte());
            }
            if ((index + count + 1) > _data.Length) return 0;
            if (count == 1) return GetBytes(result.ToArray(), 0, count)[0];
            return BitConverter.ToInt32(GetBytes(result.ToArray(), 0, count), 0);
        }

        public string GetString(int count)
        {
            var result = new List<byte>();
            for (int i = 0; i < count; i++)
            {
                result.Add(ReadByte());
            }

            return Encoding.ASCII.GetString(result.ToArray());
        }

        public int GetInt32(int count = 4)
        {
            var result = new List<byte>();
            for (int i = 0 ; i < count; i++)
            {
                result.Add(ReadByte());
            }
            if ((index + count + 1) > _data.Length) return 0;
            if (count == 1) return GetBytes(result.ToArray(), 0, count)[0];
            return BitConverter.ToInt32(GetBytes(result.ToArray(), 0, count), 0);
        }
    }
}
