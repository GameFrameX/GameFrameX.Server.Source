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
/// 本地化键常量定义 - Exceptions 分部类
/// </summary>
public static partial class Keys
{
    /// <summary>
    /// 异常消息资源键
    /// </summary>
    public static class Exceptions
    {

        /// <summary>
        /// 计时器参数无效的错误消息
        /// </summary>
        /// <remarks>
        /// 键名: Exceptions.Timer.InvalidParameters
        /// 用途: 当创建计时器时传入无效参数时抛出此异常
        /// 参数: {0} - 参数名称, {1} - 参数值
        /// </remarks>
        public const string Timer_Invalid_Parameters = "Exceptions.Timer.InvalidParameters";

        /// <summary>
        /// 组件代理映射中未找到对应条目的错误消息
        /// </summary>
        /// <remarks>
        /// 键名: Exceptions.Hotfix.ComponentAgentMapNotFound
        /// 用途: 获取组件代理时在映射表中找不到对应条目时抛出此异常
        /// 参数: {0} - 映射字段名称, {1} - 组件类型全名
        /// </remarks>
        public const string Component_Agent_Map_Not_Found = "Exceptions.Hotfix.ComponentAgentMapNotFound";

        /// <summary>
        /// BigInteger异常消息资源键
        /// </summary>
        public static class BigInteger
        {
            /// <summary>
            /// BigInteger构造函数字节溢出的错误消息
            /// </summary>
            /// <remarks>
            /// 键名: Exceptions.BigInteger.ConstructorByteOverflow
            /// 用途: 当使用字节数组构造BigInteger时发生溢出时使用
            /// </remarks>
            public const string ConstructorByteOverflow = "Exceptions.BigInteger.ConstructorByteOverflow";
        }
    }
}