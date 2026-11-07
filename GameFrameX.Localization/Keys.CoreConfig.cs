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


namespace GameFrameX.Localization;

/// <summary>
/// 本地化字符串键常量类 - 主分部类
/// Localization string keys constants class - Main partial class
///</summary>
public static partial class Keys
{
    /// <summary>
    /// 核心配置模块（ByteBuf）相关消息常量
    /// Core configuration module (ByteBuf) related message constants
    ///</summary>
    public static class CoreConfig
    {
        /// <summary>
        /// 写入段时超出最大段大小限制
        /// </summary>
        /// <remarks>
        /// 键名: CoreConfig.ByteBuf.ExceedMaxSegmentSize
        /// 用途: ByteBuf.EndWriteSegment 写入段大小超过最大可编码限制时抛出
        /// </remarks>
        public const string ExceedMaxSegmentSize = "CoreConfig.ByteBuf.ExceedMaxSegmentSize";

        /// <summary>
        /// 读取段时段大小超出最大限制
        /// </summary>
        /// <remarks>
        /// 键名: CoreConfig.ByteBuf.ExceedMaxSize
        /// 用途: ByteBuf.ReadSegment 读取到的段头编码超出支持的最大大小时抛出
        /// </remarks>
        public const string ExceedMaxSize = "CoreConfig.ByteBuf.ExceedMaxSize";

        /// <summary>
        /// 段数据不足
        /// </summary>
        /// <remarks>
        /// 键名: CoreConfig.ByteBuf.SegmentDataNotEnough
        /// 用途: ByteBuf.ReadSegment 读取后读索引超过写索引（数据不足）时抛出
        /// </remarks>
        public const string SegmentDataNotEnough = "CoreConfig.ByteBuf.SegmentDataNotEnough";
    }
}
