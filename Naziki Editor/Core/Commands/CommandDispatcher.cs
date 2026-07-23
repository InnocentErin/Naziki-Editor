using System;
using System.Collections.Generic;
using Naziki_Editor.Core.Abstractions;

namespace Naziki_Editor.Core.Commands
{
    /// <summary>
    /// 命令调度器实现，基于字典管理命令注册与执行。
    /// </summary>
    public class CommandDispatcher : ICommandDispatcher
    {
        private readonly Dictionary<string, (Action Execute, Func<bool>? CanExecute)> _commands = new Dictionary<string, (Action, Func<bool>?)>();

        public void Register(string commandName, Action execute, Func<bool>? canExecute = null)
        {
            if (string.IsNullOrEmpty(commandName)) throw new ArgumentException("命令名称不能为空", nameof(commandName));
            if (execute == null) throw new ArgumentNullException(nameof(execute));

            _commands[commandName] = (execute, canExecute);
        }

        public bool CanExecute(string commandName)
        {
            if (!_commands.TryGetValue(commandName, out var entry)) return false;
            return entry.CanExecute?.Invoke() ?? true;
        }

        public void Execute(string commandName)
        {
            if (!_commands.TryGetValue(commandName, out var entry))
                throw new InvalidOperationException($"未注册的命令：{commandName}");

            if (!CanExecute(commandName))
                throw new InvalidOperationException($"命令当前不可用：{commandName}");

            entry.Execute();
        }
    }
}
