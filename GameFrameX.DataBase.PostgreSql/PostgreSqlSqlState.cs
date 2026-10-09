// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
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
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:   https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System;
using Npgsql;

namespace GameFrameX.DataBase.PostgreSql;

/// <summary>
/// PostgreSQL SQLSTATE 常量与异常链判定（程序集内共享的单一事实源）。
/// </summary>
/// <remarks>
/// The PostgreSQL SQLSTATE constants and the exception-chain predicates: the single
/// source shared by the discovery stores and the CacheState service (formerly one
/// private constant / helper copy per file).
/// </remarks>
internal static class PostgreSqlSqlState
{
    /// <summary>
    /// PostgreSQL「同名对象已存在」SQLSTATE（42P07 duplicate_table）：EF 生成 CREATE 的运行时幂等依据。
    /// </summary>
    /// <remarks>
    /// The PostgreSQL "duplicate table" SQLSTATE (42P07): what makes the EF-generated
    /// CREATE idempotent at runtime (an existing legacy table counts as success).
    /// </remarks>
    public const string DuplicateTable = "42P07";

    /// <summary>
    /// PostgreSQL「唯一约束冲突」SQLSTATE（23505 unique_violation）。
    /// </summary>
    /// <remarks>
    /// The PostgreSQL "unique constraint violation" SQLSTATE (23505).
    /// </remarks>
    public const string UniqueViolation = "23505";

    /// <summary>
    /// 判断异常链中是否含主键唯一冲突（SQLSTATE 23505）。
    /// </summary>
    /// <remarks>
    /// Determines whether the exception chain contains a unique-constraint violation (SQLSTATE 23505).
    /// </remarks>
    /// <param name="exception">异常 / The exception</param>
    /// <returns>是否唯一冲突 / Whether a unique violation</returns>
    public static bool IsUniqueViolation(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: UniqueViolation, })
            {
                return true;
            }
        }

        return false;
    }
}