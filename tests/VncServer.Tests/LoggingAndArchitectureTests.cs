using System.Drawing;
using VncServer.Core.Codecs;
using VncServer.Core.Logging;
using Xunit;

namespace VncServer.Tests;

public class LoggingAndArchitectureTests
{
    [Fact]
    public void CompositeLogger_DispatchesToAllLoggers()
    {
        var messages1 = new List<string>();
        var messages2 = new List<string>();

        var logger1 = new ActionVncLogger(msg => messages1.Add(msg));
        var logger2 = new ActionVncLogger(msg => messages2.Add(msg));
        var composite = new CompositeVncLogger(logger1, logger2);

        composite.Info("Test info message");
        composite.Warn("Test warning message");

        Assert.Equal(2, messages1.Count);
        Assert.Equal(2, messages2.Count);
        Assert.Contains("Test info message", messages1[0]);
        Assert.Contains("Test warning message", messages1[1]);
    }

    [Fact]
    public void FileVncLogger_WritesAndRotatesLogFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ThanhVnc_Test_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        try
        {
            string logFile = Path.Combine(tempDir, "test.log");
            // Set max file size small (500 bytes) to trigger rotation
            var logger = new FileVncLogger(logFile, maxFileSizeBytes: 500, maxArchivedFiles: 3);

            for (int i = 0; i < 50; i++)
            {
                logger.Info($"This is log message number {i} containing enough bytes to exceed threshold.");
            }

            Assert.True(File.Exists(logFile));
            string archive1 = Path.Combine(tempDir, "test.1.log");
            Assert.True(File.Exists(archive1), "First rotated archive file should exist");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void CrashReporter_WritesReportFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ThanhVnc_Crash_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        try
        {
            CrashReporter.Initialize(tempDir);
            var testEx = new InvalidOperationException("Simulated unit test crash");
            CrashReporter.WriteCrashDump("UnitTest", testEx, false);

            var files = Directory.GetFiles(tempDir, "crash_*.txt");
            Assert.NotEmpty(files);

            string content = File.ReadAllText(files[0]);
            Assert.Contains("Simulated unit test crash", content);
            Assert.Contains("InvalidOperationException", content);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void JpegFrameEncoder_EncodesBitmapToValidJpeg()
    {
        using var encoder = new JpegFrameEncoder();
        using var bmp = new Bitmap(100, 100);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Blue);
        }

        byte[] jpegBytes = encoder.Encode(bmp, 80);

        Assert.NotNull(jpegBytes);
        Assert.True(jpegBytes.Length > 100);
        // Valid JPEG starts with SOI: 0xFF, 0xD8
        Assert.Equal(0xFF, jpegBytes[0]);
        Assert.Equal(0xD8, jpegBytes[1]);
        // Valid JPEG ends with EOI: 0xFF, 0xD9
        Assert.Equal(0xFF, jpegBytes[^2]);
        Assert.Equal(0xD9, jpegBytes[^1]);
    }
}
