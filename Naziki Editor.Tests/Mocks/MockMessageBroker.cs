using Naziki_Editor.Core.Abstractions;
using System;
using System.Collections.Generic;

namespace Naziki_Editor.Tests.Mocks
{
    /// <summary>
    /// 模拟 MessageBroker，用于测试中捕获发布的消息
    /// </summary>
    public class MockMessageBroker : IMessageBroker
    {
        public List<(string Topic, object? Data)> PublishedMessages { get; } = new();

        public void Subscribe<T>(string topic, Action<T> handler) { }
        public void Subscribe(string topic, Action handler) { }

        public void Publish<T>(string topic, T data)
        {
            PublishedMessages.Add((topic, data));
        }

        public void Publish(string topic)
        {
            PublishedMessages.Add((topic, null));
        }
    }
}