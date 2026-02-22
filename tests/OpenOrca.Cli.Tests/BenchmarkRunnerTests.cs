using OpenOrca.Cli.Repl;
using Xunit;

namespace OpenOrca.Cli.Tests;

public class BenchmarkRunnerTests
{
    // ── ValidateOutput ──

    [Fact]
    public void ValidateOutput_AllPrimesCorrect_ReturnsPass()
    {
        var primes = "2\n3\n5\n7\n11\n13\n17\n19\n23\n29\n31\n37\n41\n43\n47\n53\n59\n61\n67\n71\n73\n79\n83\n89\n97\n";
        var (correct, notes) = BenchmarkRunner.ValidateOutput(primes);
        Assert.True(correct);
        Assert.Contains("25 primes correct", notes);
    }

    [Fact]
    public void ValidateOutput_MissingPrimes_ReturnsFail()
    {
        var primes = "2\n3\n5\n7\n11\n";
        var (correct, notes) = BenchmarkRunner.ValidateOutput(primes);
        Assert.False(correct);
        Assert.Contains("Missing:", notes);
        Assert.Contains("97", notes);
    }

    [Fact]
    public void ValidateOutput_ExtraNumbers_ReturnsFail()
    {
        var primes = "2\n3\n5\n7\n11\n13\n17\n19\n23\n29\n31\n37\n41\n43\n47\n53\n59\n61\n67\n71\n73\n79\n83\n89\n97\n100\n";
        var (correct, notes) = BenchmarkRunner.ValidateOutput(primes);
        Assert.False(correct);
        Assert.Contains("Extra:", notes);
        Assert.Contains("100", notes);
    }

    [Fact]
    public void ValidateOutput_EmptyFile_ReturnsFail()
    {
        var (correct, notes) = BenchmarkRunner.ValidateOutput("");
        Assert.False(correct);
        Assert.Contains("empty", notes!);
    }

    [Fact]
    public void ValidateOutput_WhitespaceOnly_ReturnsFail()
    {
        var (correct, notes) = BenchmarkRunner.ValidateOutput("   \n\n  ");
        Assert.False(correct);
        Assert.Contains("empty", notes!);
    }

    [Fact]
    public void ValidateOutput_NoNumbers_ReturnsFail()
    {
        var (correct, notes) = BenchmarkRunner.ValidateOutput("hello\nworld\n");
        Assert.False(correct);
        Assert.Contains("No numbers", notes!);
    }

    [Fact]
    public void ValidateOutput_WindowsLineEndings_Works()
    {
        var primes = "2\r\n3\r\n5\r\n7\r\n11\r\n13\r\n17\r\n19\r\n23\r\n29\r\n31\r\n37\r\n41\r\n43\r\n47\r\n53\r\n59\r\n61\r\n67\r\n71\r\n73\r\n79\r\n83\r\n89\r\n97\r\n";
        var (correct, _) = BenchmarkRunner.ValidateOutput(primes);
        Assert.True(correct);
    }

    // ── FindPythonEntryPoint ──

    [Fact]
    public void FindPythonEntryPoint_MainPyExists_ReturnsMainPy()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "main.py"), "print('hello')");
            File.WriteAllText(Path.Combine(dir, "other.py"), "print('other')");

            var result = BenchmarkRunner.FindPythonEntryPoint(dir);
            Assert.NotNull(result);
            Assert.Equal(Path.Combine(dir, "main.py"), result);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FindPythonEntryPoint_SinglePyFile_ReturnsThatFile()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "app.py"), "print('hello')");

            var result = BenchmarkRunner.FindPythonEntryPoint(dir);
            Assert.NotNull(result);
            Assert.EndsWith("app.py", result);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FindPythonEntryPoint_MultipleCandidates_ReturnsNull()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "app.py"), "print('hello')");
            File.WriteAllText(Path.Combine(dir, "utils.py"), "print('utils')");

            var result = BenchmarkRunner.FindPythonEntryPoint(dir);
            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FindPythonEntryPoint_TestFilesExcluded()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "app.py"), "print('hello')");
            File.WriteAllText(Path.Combine(dir, "test.py"), "assert True");
            File.WriteAllText(Path.Combine(dir, "test_app.py"), "assert True");
            File.WriteAllText(Path.Combine(dir, "app_test.py"), "assert True");
            File.WriteAllText(Path.Combine(dir, "setup.py"), "setup()");
            File.WriteAllText(Path.Combine(dir, "conftest.py"), "");

            var result = BenchmarkRunner.FindPythonEntryPoint(dir);
            Assert.NotNull(result);
            Assert.EndsWith("app.py", result);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FindPythonEntryPoint_NoPyFiles_ReturnsNull()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "readme.txt"), "hello");

            var result = BenchmarkRunner.FindPythonEntryPoint(dir);
            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── SanitizeModelName ──

    [Theory]
    [InlineData("gpt-4", "gpt-4")]
    [InlineData("mistral-7b-instruct-v0.3", "mistral-7b-instruct-v0_3")]
    [InlineData("model/with/slashes", "model_with_slashes")]
    [InlineData("MODEL-Name", "model-name")]
    public void SanitizeModelName_ReplacesInvalidChars(string input, string expected)
    {
        var result = BenchmarkRunner.SanitizeModelName(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SanitizeModelName_TruncatesLongNames()
    {
        var longName = new string('a', 100);
        var result = BenchmarkRunner.SanitizeModelName(longName);
        Assert.Equal(50, result.Length);
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"orca-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
