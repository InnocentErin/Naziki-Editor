using System;

namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 命令调度器抽象，负责按名称注册和执行无参命令。
    /// 不包含任何 UI 依赖。
    /// </summary>
    public interface ICommandDispatcher
    {
        /// <summary>
        /// 注册一个命令。
        /// </summary>
        /// <param name="commandName">命令名称。</param>
        /// <param name="execute">命令执行委托。</param>
        /// <param name="canExecute">命令可用性判断委托，为 null 时默认可用。</param>
        void Register(string commandName, Action execute, Func<bool>? canExecute = null);

        /// <summary>
        /// 判断指定命令当前是否可以执行。
        /// </summary>
        bool CanExecute(string commandName);

        /// <summary>
        /// 执行指定命令。命令不存在时抛出异常。
        /// </summary>
        void Execute(string commandName);
    }
}
