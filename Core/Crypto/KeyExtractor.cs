using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;

namespace MSBATranslator.Core.Crypto
{
    public static class FastUniversalKeyExtractor
    {
        public readonly struct MemoryRange
        {
            public readonly ulong Va;
            public readonly ulong Size;
            public readonly ulong FileRva;

            public MemoryRange(ulong va, ulong size, ulong fileRva)
            {
                Va = va;
                Size = size;
                FileRva = fileRva;
            }
        }

        public static unsafe List<string> FindAllSqlKeysInDump(string dumpFilePath)
        {
            var results = new List<string>();
            var foundSet = new HashSet<string>();

            if (!File.Exists(dumpFilePath)) return results;

            using var mmf = MemoryMappedFile.CreateFromFile(dumpFilePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            byte* basePtr = null;
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);

            try
            {
                if (*(uint*)basePtr != 0x504D444D) return results; // "MDMP"

                uint numStreams = *(uint*)(basePtr + 8);
                uint dirRva = *(uint*)(basePtr + 12);
                long fileSize = new FileInfo(dumpFilePath).Length;

                var ranges = new List<MemoryRange>();

                for (uint i = 0; i < numStreams; i++)
                {
                    long off = dirRva + (i * 12);
                    if (off + 12 > fileSize) break;

                    uint sType = *(uint*)(basePtr + off);
                    uint sRva = *(uint*)(basePtr + off + 8);

                    if (sType == 9 && sRva + 16 <= fileSize) // Memory64ListStream
                    {
                        ulong numRanges = *(ulong*)(basePtr + sRva);
                        ulong currRva = *(ulong*)(basePtr + sRva + 8);
                        long descOffset = sRva + 16;

                        for (ulong r = 0; r < numRanges; r++)
                        {
                            if (descOffset + 16 > fileSize) break;
                            ulong va = *(ulong*)(basePtr + descOffset);
                            ulong size = *(ulong*)(basePtr + descOffset + 8);
                            ranges.Add(new MemoryRange(va, size, currRva));
                            currRva += size;
                            descOffset += 16;
                        }
                    }
                }

                if (ranges.Count == 0) return results;

                var sortedRanges = ranges.OrderBy(r => r.Va).ToArray();
                ulong minVa = sortedRanges[0].Va;
                ulong maxVa = sortedRanges[^1].Va + sortedRanges[^1].Size;

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                byte* VaToPtr(ulong va)
                {
                    if (va < minVa || va >= maxVa) return null;

                    int low = 0;
                    int high = sortedRanges.Length - 1;

                    while (low <= high)
                    {
                        int mid = low + ((high - low) >> 1);
                        ref readonly var r = ref sortedRanges[mid];

                        if (va < r.Va) high = mid - 1;
                        else if (va >= r.Va + r.Size) low = mid + 1;
                        else
                        {
                            ulong fOff = r.FileRva + (va - r.Va);
                            return (long)fOff < fileSize ? basePtr + fOff : null;
                        }
                    }
                    return null;
                }

                foreach (var range in sortedRanges)
                {
                    if (range.Size < 24) continue;
                    byte* pStart = basePtr + range.FileRva;
                    byte* pEnd = pStart + range.Size - 24;

                    for (byte* ptr = pStart; ptr <= pEnd; ptr += 8)
                    {
                        ulong p1Va = *(ulong*)ptr;
                        if (p1Va < minVa || p1Va >= maxVa || (p1Va & 7) != 0) continue;

                        byte* p1 = VaToPtr(p1Va);
                        if (p1 == null || *(uint*)(p1 + 0x18) != 10) continue;

                        ulong p2Va = *(ulong*)(ptr + 8);
                        if (p2Va < minVa || p2Va >= maxVa || (p2Va & 7) != 0) continue;

                        byte* p2 = VaToPtr(p2Va);
                        if (p2 == null || *(uint*)(p2 + 0x18) != 10) continue;

                        ulong p3Va = *(ulong*)(ptr + 16);
                        if (p3Va < minVa || p3Va >= maxVa || (p3Va & 7) != 0) continue;

                        byte* p3 = VaToPtr(p3Va);
                        if (p3 == null || *(uint*)(p3 + 0x18) != 12) continue;

                        byte[] keyBytes = new byte[32];
                        fixed (byte* dst = keyBytes)
                        {
                            Buffer.MemoryCopy(p1 + 0x20, dst, 32, 10);
                            Buffer.MemoryCopy(p2 + 0x20, dst + 10, 22, 10);
                            Buffer.MemoryCopy(p3 + 0x20, dst + 20, 12, 12);
                        }

                        string hexKey = Convert.ToHexString(keyBytes);
                        if (foundSet.Add(hexKey))
                        {
                            results.Add(hexKey);
                        }
                    }
                }

                return results;
            }
            finally
            {
                if (basePtr != null)
                    accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
    }
}