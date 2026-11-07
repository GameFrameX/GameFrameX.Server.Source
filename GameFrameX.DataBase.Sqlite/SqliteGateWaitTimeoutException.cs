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
//   please refer to the LICENSE file at the root directory of the source code for the full license text.
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


namespace GameFrameX.DataBase.Sqlite;

/// <summary>
/// SQLite 连接闸等待超时异常（不可重试：闸超时多为事务壳 action 内不可重入死锁，重试只会成倍延长挂起）。
/// </summary>
/// <remarks>
/// Thrown when the SQLite connection gate is not acquired within the configured timeout. It derives
/// from <see cref="TimeoutException"/> so callers catch it as a timeout, but the exception classifier
/// treats it as non-retryable: a gate timeout almost always means a non-reentrant deadlock inside a
/// transaction-shell action, and retrying would only multiply the hang.
/// </remarks>
internal sealed class SqliteGateWaitTimeoutException : TimeoutException
{
    /// <summary>
    /// 使用指定的错误消息初始化 <see cref="SqliteGateWaitTimeoutException"/> 类的新实例。
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="SqliteGateWaitTimeoutException"/> class with a specified error message.
    /// </remarks>
    /// <param name="message">描述错误的消息 / The message that describes the error</param>
    public SqliteGateWaitTimeoutException(string message) : base(message)
    {
    }
}
