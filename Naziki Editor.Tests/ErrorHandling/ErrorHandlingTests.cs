using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.ErrorHandling;
using Naziki_Editor.Tests.Mocks;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Naziki_Editor.Tests.ErrorHandling
{
    /// <summary>
    /// ErrorInfo 模型单元测试
    /// </summary>
    public class ErrorInfoTests
    {
        [Fact]
        public void Constructor_ShouldSetAllProperties()
        {
            var ex = new InvalidOperationException("test error");
            var errorInfo = new ErrorInfo(
                ErrorSeverity.Error,
                "FileIO",
                "文件读取失败",
                "TestClass.TestMethod",
                ex,
                "FilePath: C:\\test.txt");

            Assert.Equal(ErrorSeverity.Error, errorInfo.Severity);
            Assert.Equal("FileIO", errorInfo.ErrorType);
            Assert.Equal("文件读取失败", errorInfo.Description);
            Assert.Equal("TestClass.TestMethod", errorInfo.Location);
            Assert.Equal(ex, errorInfo.OriginalException);
            Assert.Equal("FilePath: C:\\test.txt", errorInfo.ContextData);
            Assert.False(string.IsNullOrEmpty(errorInfo.ErrorId));
            Assert.Equal(8, errorInfo.ErrorId.Length);
            Assert.True((DateTime.Now - errorInfo.Timestamp).TotalSeconds < 5);
        }

        [Fact]
        public void Constructor_WithoutException_ShouldNotThrow()
        {
            var errorInfo = new ErrorInfo(
                ErrorSeverity.Warning,
                "DataValidation",
                "输入数据为空",
                "Validator.Validate");

            Assert.Null(errorInfo.OriginalException);
            Assert.Null(errorInfo.ContextData);
        }

        [Fact]
        public void ToString_ShouldContainAllKeyInfo()
        {
            var ex = new IOException("disk full");
            var errorInfo = new ErrorInfo(
                ErrorSeverity.Critical,
                "FileIO",
                "磁盘写入失败",
                "FileService.Save",
                ex,
                "Path: C:\\data.json");

            var output = errorInfo.ToString();

            Assert.Contains("[Critical]", output);
            Assert.Contains("[FileIO]", output);
            Assert.Contains("磁盘写入失败", output);
            Assert.Contains("FileService.Save", output);
            Assert.Contains("IOException", output);
            Assert.Contains("disk full", output);
            Assert.Contains("Path: C:\\data.json", output);
        }
    }

    /// <summary>
    /// ErrorHandler 单元测试
    /// </summary>
    public class ErrorHandlerTests
    {
        private readonly MockMessageBroker _messageBroker;
        private readonly ErrorHandler _errorHandler;

        public ErrorHandlerTests()
        {
            _messageBroker = new MockMessageBroker();
            _errorHandler = new ErrorHandler(_messageBroker);
        }

        [Fact]
        public void HandleError_ShouldAddToLog()
        {
            var errorInfo = new ErrorInfo(
                ErrorSeverity.Error,
                "Test",
                "测试错误",
                "Test.Location");

            _errorHandler.HandleError(errorInfo);

            var log = _errorHandler.GetErrorLog();
            Assert.Contains(errorInfo, log);
        }

        [Fact]
        public void HandleError_ShouldPublishToMessageBroker()
        {
            var errorInfo = new ErrorInfo(
                ErrorSeverity.Error,
                "Test",
                "测试错误",
                "Test.Location");

            _errorHandler.HandleError(errorInfo);

            Assert.Contains(_messageBroker.PublishedMessages,
                m => m.Topic == "Error.Occurred" && m.Data == errorInfo);
        }

        [Fact]
        public void HandleError_Critical_ShouldPublishCriticalTopic()
        {
            var errorInfo = new ErrorInfo(
                ErrorSeverity.Critical,
                "Test",
                "严重错误",
                "Test.Location");

            _errorHandler.HandleError(errorInfo);

            Assert.Contains(_messageBroker.PublishedMessages,
                m => m.Topic == "Error.Critical" && m.Data == errorInfo);
        }

        [Fact]
        public void HandleError_Warning_ShouldPublishWarningTopic()
        {
            var errorInfo = new ErrorInfo(
                ErrorSeverity.Warning,
                "Test",
                "警告",
                "Test.Location");

            _errorHandler.HandleError(errorInfo);

            Assert.Contains(_messageBroker.PublishedMessages,
                m => m.Topic == "Error.Warning" && m.Data == errorInfo);
        }

        [Fact]
        public void HandleError_Info_ShouldPublishInfoTopic()
        {
            var errorInfo = new ErrorInfo(
                ErrorSeverity.Info,
                "Test",
                "信息",
                "Test.Location");

            _errorHandler.HandleError(errorInfo);

            Assert.Contains(_messageBroker.PublishedMessages,
                m => m.Topic == "Error.Info" && m.Data == errorInfo);
        }

        [Fact]
        public void HandleError_Null_ShouldNotThrow()
        {
            var ex = Record.Exception(() => _errorHandler.HandleError(null!));
            Assert.Null(ex);
        }

        [Fact]
        public void HandleException_ShouldCreateAndHandleError()
        {
            var ex = new InvalidOperationException("操作无效");

            _errorHandler.HandleException(
                ex,
                ErrorSeverity.Error,
                "DataValidation",
                "数据校验失败",
                "Validator.Check",
                "Input: 123");

            var log = _errorHandler.GetErrorLog();
            Assert.Single(log);
            Assert.Equal(ErrorSeverity.Error, log[0].Severity);
            Assert.Equal("DataValidation", log[0].ErrorType);
            Assert.Equal("数据校验失败", log[0].Description);
            Assert.Equal("Validator.Check", log[0].Location);
            Assert.Equal(ex, log[0].OriginalException);
            Assert.Equal("Input: 123", log[0].ContextData);
        }

        [Fact]
        public void OnErrorOccurred_ShouldFireCallback()
        {
            ErrorInfo? capturedError = null;
            _errorHandler.OnErrorOccurred += (e) => capturedError = e;

            var errorInfo = new ErrorInfo(
                ErrorSeverity.Error,
                "Test",
                "回调测试",
                "Test.Callback");

            _errorHandler.HandleError(errorInfo);

            Assert.NotNull(capturedError);
            Assert.Equal("回调测试", capturedError!.Description);
        }

        [Fact]
        public void TryExecute_Action_Success_ShouldReturnTrue()
        {
            bool executed = false;
            var result = _errorHandler.TryExecute(() =>
            {
                executed = true;
            }, "Test", "Test.TryExecute");

            Assert.True(result);
            Assert.True(executed);
            Assert.Empty(_errorHandler.GetErrorLog());
        }

        [Fact]
        public void TryExecute_Action_Throws_ShouldReturnFalse()
        {
            var result = _errorHandler.TryExecute(() =>
            {
                throw new InvalidOperationException("测试异常");
            }, "Test", "Test.TryExecute");

            Assert.False(result);
            var log = _errorHandler.GetErrorLog();
            Assert.Single(log);
            Assert.Equal(ErrorSeverity.Error, log[0].Severity);
            Assert.Contains("测试异常", log[0].Description);
        }

        [Fact]
        public void TryExecute_Func_Success_ShouldReturnValue()
        {
            var result = _errorHandler.TryExecute(() => 42, "Test", "Test.TryExecute");

            Assert.Equal(42, result);
            Assert.Empty(_errorHandler.GetErrorLog());
        }

        [Fact]
        public void TryExecute_Func_Throws_ShouldReturnDefault()
        {
            var result = _errorHandler.TryExecute<int>(() =>
            {
                throw new Exception("测试异常");
            }, "Test", "Test.TryExecute");

            Assert.Equal(0, result);
            var log = _errorHandler.GetErrorLog();
            Assert.Single(log);
        }

        [Fact]
        public void GetErrorLog_ShouldReturnSnapshot()
        {
            for (int i = 0; i < 5; i++)
            {
                _errorHandler.HandleError(new ErrorInfo(
                    ErrorSeverity.Info,
                    "Test",
                    $"Error {i}",
                    "Test.Location"));
            }

            var log = _errorHandler.GetErrorLog();
            Assert.Equal(5, log.Count);
        }

        [Fact]
        public void ErrorLog_ShouldNotExceedMaxSize()
        {
            // 添加超过最大容量的错误
            for (int i = 0; i < 1100; i++)
            {
                _errorHandler.HandleError(new ErrorInfo(
                    ErrorSeverity.Debug,
                    "Test",
                    $"Error {i}",
                    "Test.Location"));
            }

            var log = _errorHandler.GetErrorLog();
            Assert.True(log.Count <= 1000);
        }
    }

    /// <summary>
    /// ErrorSeverity 枚举测试
    /// </summary>
    public class ErrorSeverityTests
    {
        [Fact]
        public void AllSeverities_ShouldBeDefined()
        {
            var values = Enum.GetValues<ErrorSeverity>();
            Assert.Equal(6, values.Length);
            Assert.Contains(ErrorSeverity.Debug, values);
            Assert.Contains(ErrorSeverity.Info, values);
            Assert.Contains(ErrorSeverity.Warning, values);
            Assert.Contains(ErrorSeverity.Error, values);
            Assert.Contains(ErrorSeverity.Critical, values);
            Assert.Contains(ErrorSeverity.Fatal, values);
        }
    }
}