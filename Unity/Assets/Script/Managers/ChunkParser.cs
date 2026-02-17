using System;
using System.Text.RegularExpressions;
using System.Linq;

namespace Script.Managers
{
    public static class ChunkParser
    {
        // 从 sanitized 文本中提取 "tiles" 数组数字，返回长度为 expected 的数组（不足填 0，多余截断）
        public static int[] ParseTilesFallback(string sanitized, int expected)
        {
            var result = new int[Math.Max(0, expected)];
            if (expected <= 0 || string.IsNullOrEmpty(sanitized)) return result;

            var mt = Regex.Match(sanitized, "\"tiles\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            if (!mt.Success) return result;

            var nums = Regex.Matches(mt.Groups[1].Value, "-?\\d+");
            int take = Math.Min(nums.Count, expected);
            for (int i = 0; i < take; i++)
            {
                if (!int.TryParse(nums[i].Value, out result[i]))
                {
                    result[i] = 0; // 解析失败则填 0
                }
            }
            return result;
        }

        // 从 sanitized 文本中提取指定 key 的数字数组并作为 byte[] 返回（不足填 0，多余截断）
        public static byte[] ParseByteArrayFallback(string sanitized, string key, int expected)
        {
            var result = new byte[Math.Max(0, expected)];
            if (expected <= 0 || string.IsNullOrEmpty(sanitized) || string.IsNullOrEmpty(key)) return result;

            string keyEscaped = Regex.Escape(key);
            string pattern = "\"" + keyEscaped + "\"" + @"\s*:\s*\[(.*?)\]";
            var m = Regex.Match(sanitized, pattern, RegexOptions.Singleline);
            if (!m.Success) return result;

            var nums = Regex.Matches(m.Groups[1].Value, "-?\\d+");
            int take = Math.Min(nums.Count, expected);
            for (int i = 0; i < take; i++)
            {
                if (byte.TryParse(nums[i].Value, out byte b)) result[i] = b; else result[i] = 0;
            }
            return result;
        }

        // 示例用法
        public static void Example()
        {
            string sanitized = "{\"width\":4,\"height\":4,\"tiles\":[1,2,3,4,5,6,7,8,9]}";
            int expected = 4 * 4; // 期望 16 个
            int[] tiles = ParseTilesFallback(sanitized, expected);
            Console.WriteLine($"tiles.len={tiles.Length} sample={string.Join(",", tiles.Select(i => i.ToString()))}");
        }
    }
}
