using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;

namespace MSBATranslator.Core.Crypto
{
    public static class Crc32Patcher
    {
        private const ulong GfPolynomial = 0x104C11DB7UL;
        private const uint ModularInverseX32 = 0xCBF1ACDA;
        private const uint StandardInitVector = 0xFFFFFFFF;
        private const int BufferSize = 128 * 1024;

        private static readonly uint[] Table = GenerateLookupTable();

        private static uint[] GenerateLookupTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++)
                {
                    value = (value & 1) != 0 ? (value >> 1) ^ 0xEDB88320U : (value >> 1);
                }
                table[i] = value;
            }
            return table;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Compute(ReadOnlySpan<byte> data)
        {
            return ~UpdateRunningCrc(data, StandardInitVector);
        }

        public static uint ComputeFromFile(string path)
        {
            if (!File.Exists(path)) return 0;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
                uint state = StandardInitVector;
                int bytesRead;

                while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    state = UpdateRunningCrc(buffer.AsSpan(0, bytesRead), state);
                }

                return ~state;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint UpdateRunningCrc(ReadOnlySpan<byte> buffer, uint state)
        {
            ref uint tableRef = ref Table[0];
            foreach (byte octet in buffer)
            {
                state = (state >> 8) ^ Unsafe.Add(ref tableRef, (state ^ octet) & 0xFF);
            }
            return state;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint FlipBits32(uint value)
        {
            value = ((value >> 1) & 0x55555555U) | ((value & 0x55555555U) << 1);
            value = ((value >> 2) & 0x33333333U) | ((value & 0x33333333U) << 2);
            value = ((value >> 4) & 0x0F0F0F0FU) | ((value & 0x0F0F0F0FU) << 4);
            value = ((value >> 8) & 0x00FF00FFU) | ((value & 0x00FF00FFU) << 8);
            return (value >> 16) | (value << 16);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte FlipByte(byte b)
        {
            return (byte)((((b * 0x80200802UL) & 0x0884422110UL) * 0x0101010101UL) >> 32);
        }

        private static uint PolyMultiplyGf2(uint factorA, uint factorB)
        {
            ulong accumulator = 0;
            ulong termA = factorA;
            ulong termB = factorB;

            while (termB > 0)
            {
                if ((termB & 1) != 0)
                    accumulator ^= termA;

                termB >>= 1;
                termA <<= 1;

                if ((termA & (1UL << 32)) != 0)
                    termA ^= GfPolynomial;
            }

            return (uint)accumulator;
        }

        public static void CalculateCorrectionBytes(uint runningState, uint targetChecksum, Span<byte> destination4Bytes)
        {
            if (destination4Bytes.Length < 4)
                throw new ArgumentException("Buffer must be at least 4 bytes.", nameof(destination4Bytes));

            uint state = runningState;
            state = (state >> 8) ^ Table[state & 0xFF];
            state = (state >> 8) ^ Table[state & 0xFF];
            state = (state >> 8) ^ Table[state & 0xFF];
            state = (state >> 8) ^ Table[state & 0xFF];

            uint checksumWithZeros = ~state;
            uint difference = FlipBits32(targetChecksum ^ checksumWithZeros);
            uint solution = PolyMultiplyGf2(difference, ModularInverseX32);

            destination4Bytes[0] = FlipByte((byte)(solution >> 24));
            destination4Bytes[1] = FlipByte((byte)(solution >> 16));
            destination4Bytes[2] = FlipByte((byte)(solution >> 8));
            destination4Bytes[3] = FlipByte((byte)solution);
        }

        public static byte[] AttachCorrectionBytes(byte[] sourceData, uint targetChecksum)
        {
            uint runningState = UpdateRunningCrc(sourceData, StandardInitVector);

            byte[] patched = new byte[sourceData.Length + 4];
            sourceData.CopyTo(patched, 0);

            CalculateCorrectionBytes(runningState, targetChecksum, patched.AsSpan(sourceData.Length, 4));
            return patched;
        }

        public static bool SyncFileChecksum(string destinationFile, string referenceFile)
        {
            if (!File.Exists(destinationFile) || !File.Exists(referenceFile))
            {
                Logger.Log("- Ошибка: один или оба файла для согласования CRC не найдены.");
                return false;
            }

            try
            {
                uint expectedCrc = ComputeFromFile(referenceFile);

                uint destState = StandardInitVector;
                byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

                try
                {
                    using (var fs = new FileStream(destinationFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None, BufferSize))
                    {
                        int bytesRead;
                        while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            destState = UpdateRunningCrc(buffer.AsSpan(0, bytesRead), destState);
                        }

                        Span<byte> fixBytes = stackalloc byte[4];
                        CalculateCorrectionBytes(destState, expectedCrc, fixBytes);

                        fs.Seek(0, SeekOrigin.End);
                        fs.Write(fixBytes);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                Logger.Log($"+ Контрольная сумма: {expectedCrc:X8} - успешно согласована");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"- Ошибка генерации CRC: {ex.Message}");
                return false;
            }
        }
    }
}