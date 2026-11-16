using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>组合/模块导入测试共用进程级临时目录，必须串行，且不与其他集合并发。</summary>
[CollectionDefinition(nameof(FileSystemImportTests), DisableParallelization = true)]
public sealed class FileSystemImportTests;
