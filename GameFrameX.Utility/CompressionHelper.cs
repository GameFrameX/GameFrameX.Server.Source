// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Buffers;
using GameFrameX.Foundation.Localization.Core;
using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Zip.Compression;

namespace GameFrameX.Utility;

/// <summary>
/// Compression and decompression helper.
/// </summary>
/// <remarks>
/// 压缩解压缩辅助器。
/// </remarks>
public static class CompressionHelper
{
    /// <summary>
    /// Buffer size in bytes for compression and decompression operations
    /// </summary>
    /// <remarks>
    /// 用于压缩和解压缩操作的缓冲区大小（以字节为单位）
    /// </remarks>
    private const int BufferSize = 8192;

    /// <summary>
    /// Default maximum decompressed output length: 64 MB. Used when the legacy <see cref="Decompress(byte[])"/> overload
    /// is invoked without an explicit caller-declared limit; guards against decompression bombs (a small highly-compressed
    /// frame otherwise inflates into gigabytes of managed memory before any post-hoc length check runs).
    /// </summary>
    /// <remarks>
    /// 默认解压输出上限：64MB。供旧重载 <see cref="Decompress(byte[])"/> 在调用方未显式声明上限时使用；
    /// 用于防御解压炸弹（高压缩比小帧若不设上限，会在任何事后长度检查之前膨胀为数 GB 托管内存）。
    /// 调用方已知协议级最大消息体时应显式传入更贴身的上限。
    /// </remarks>
    public const long DefaultMaxDecompressedOutputLength = 64 * 1024 * 1024;

    /// <summary>
    /// Compresses data. Uses the Deflate algorithm to compress the original byte array into a smaller byte array.
    /// Failures propagate the original exception (no silent fallback returning the input).
    /// </summary>
    /// <remarks>
    /// 压缩数据。使用Deflate算法将原始字节数组压缩成更小的字节数组。失败时异常原样抛出，不静默回退返回原始字节。
    /// </remarks>
    /// <param name="bytes">The original byte array to be compressed. Cannot be null. / 要压缩的原始字节数组。不能为null。</param>
    /// <returns>The compressed byte array. If the input is an empty array, returns the empty array directly. / 压缩后的字节数组。如果输入为空数组，则直接返回该空数组。</returns>
    /// <exception cref="ArgumentNullException">Thrown when the input parameter bytes is null. / 当输入参数bytes为null时抛出。</exception>
    public static byte[] Compress(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
        {
            return bytes;
        }

        var compressor = new Deflater();
        compressor.SetLevel(Deflater.BEST_COMPRESSION);

        compressor.SetInput(bytes);
        compressor.Finish();

        using (var compressorMemoryStream = new MemoryStream(bytes.Length))
        {
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                while (!compressor.IsFinished)
                {
                    var count = compressor.Deflate(buffer);
                    compressorMemoryStream.Write(buffer, 0, count);
                }

                return compressorMemoryStream.ToArray();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    /// <summary>
    /// Decompresses data. Uses the Inflate algorithm to restore the compressed byte array to the original byte array,
    /// with the default output limit <see cref="DefaultMaxDecompressedOutputLength"/>.
    /// </summary>
    /// <remarks>
    /// 解压数据。使用Inflate算法将压缩的字节数组还原成原始字节数组，输出上限取默认值 <see cref="DefaultMaxDecompressedOutputLength"/>。
    /// </remarks>
    /// <param name="bytes">The compressed byte array to be decompressed. Cannot be null. / 要解压的压缩字节数组。不能为null。</param>
    /// <returns>The decompressed original byte array. If the input is an empty array, returns the empty array directly. / 解压后的原始字节数组。如果输入为空数组，则直接返回该空数组。</returns>
    /// <exception cref="ArgumentNullException">Thrown when the input parameter bytes is null. / 当输入参数bytes为null时抛出。</exception>
    /// <exception cref="InvalidDataException">Thrown when the compressed data format is invalid, corrupted or truncated, or when the decompressed output exceeds the default limit. / 当压缩数据格式无效、已损坏或被截断，或解压输出超过默认上限时抛出。</exception>
    public static byte[] Decompress(byte[] bytes)
    {
        return Decompress(bytes, DefaultMaxDecompressedOutputLength);
    }

    /// <summary>
    /// Decompresses data with an explicit output limit. Uses the Inflate algorithm to restore the compressed byte array
    /// to the original byte array; the loop aborts as soon as the accumulated output exceeds
    /// <paramref name="maxOutputLength"/>, so the inflation itself is bounded instead of being checked only after completion.
    /// </summary>
    /// <remarks>
    /// 解压数据（显式输出上限）。使用Inflate算法将压缩的字节数组还原成原始字节数组；
    /// 累积输出一旦超过 <paramref name="maxOutputLength"/> 立即中止并抛出异常——上限约束作用于膨胀过程本身，
    /// 而不是在解压完成后才检查。调用方（网络消息解压入口）应传入各自协议的最大消息体上限。
    /// </remarks>
    /// <param name="bytes">The compressed byte array to be decompressed. Cannot be null. / 要解压的压缩字节数组。不能为null。</param>
    /// <param name="maxOutputLength">Maximum allowed decompressed output length in bytes; must not be negative. / 允许的最大解压输出字节数，不得为负。</param>
    /// <returns>The decompressed original byte array. If the input is an empty array, returns the empty array directly. / 解压后的原始字节数组。如果输入为空数组，则直接返回该空数组。</returns>
    /// <exception cref="ArgumentNullException">Thrown when the input parameter bytes is null. / 当输入参数bytes为null时抛出。</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when maxOutputLength is negative. / 当maxOutputLength为负数时抛出。</exception>
    /// <exception cref="InvalidDataException">Thrown when the compressed data format is invalid, corrupted or truncated, or when the decompressed output exceeds maxOutputLength. / 当压缩数据格式无效、已损坏或被截断，或解压输出超过maxOutputLength时抛出。</exception>
    public static byte[] Decompress(byte[] bytes, long maxOutputLength)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxOutputLength);
        if (bytes.Length == 0)
        {
            return bytes;
        }

        var decompressor = new Inflater();
        decompressor.SetInput(bytes, 0, bytes.Length);
        using (var decompressMemoryStream = new MemoryStream(bytes.Length))
        {
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                while (!decompressor.IsFinished)
                {
                    var count = decompressor.Inflate(buffer);
                    if (count == 0 && (decompressor.IsNeedingInput || decompressor.IsNeedingDictionary))
                    {
                        // SharpZipLib 对截断流 Inflate 恒返回 0 且 IsFinished 恒 false，必须主动判定，否则死循环。
                        // Localization: Utility.CompressionHelper.DecompressInputTruncated - 压缩数据已被截断，无法完成解压。
                        throw new InvalidDataException(LocalizationService.GetString(Localization.Keys.Utility.CompressionHelper.DecompressInputTruncated));
                    }

                    decompressMemoryStream.Write(buffer, 0, count);
                    if (decompressMemoryStream.Length > maxOutputLength)
                    {
                        // 解压炸弹防线：上限检查必须在膨胀过程内，事后检查等于没有检查。
                        // Localization: Utility.CompressionHelper.DecompressedSizeExceedsLimit - 解压输出超过允许的最大长度：{0}。
                        throw new InvalidDataException(LocalizationService.GetString(Localization.Keys.Utility.CompressionHelper.DecompressedSizeExceedsLimit, maxOutputLength));
                    }
                }

                return decompressMemoryStream.ToArray();
            }
            catch (SharpZipBaseException e)
            {
                // Localization: Utility.CompressionHelper.DecompressFailed - CompressionHelper.Decompress 压缩辅助类异常。错误：{0}。
                throw new InvalidDataException(LocalizationService.GetString(Localization.Keys.Utility.CompressionHelper.DecompressFailed, e.ToString()), e);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, true);
            }
        }
    }
}
