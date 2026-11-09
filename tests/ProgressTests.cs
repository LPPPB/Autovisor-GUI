using System;
using System.IO;
using System.Text;
using AutovisorPanel;

internal static class ProgressTests
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    private static void Equal(object expected, object actual)
    {
        if (!object.Equals(expected, actual))
        {
            Console.Error.WriteLine("Expected " + expected + ", got " + actual);
            Environment.Exit(1);
        }
    }

    private static void Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "autovisor-progress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "Log1.txt");
        LogTail tail = new LogTail();
        File.WriteAllText(file,
            "[19:50:26] [EVENT] [main:298] 课程目录 | 类型=fusion\n" +
            "[19:50:27] [EVENT] [main:310] 当前课程 | 名称=示例课程\n" +
            "[19:50:28] [DEBUG] [utils:99] Get to-learn class: 30 / 106\n" +
            "[19:50:28] [EVENT] [runner:71] 目录筛选 | 模式=学习 待学课时=30 本轮课时=30\n" +
            "[19:50:29] [EVENT] [runner:109] 课时开始 | 序号=1/30 标题=示例小节 平台进度=12%\n", Utf8);
        StudySnapshot s = tail.Poll(dir, true);
        Equal("示例课程", s.CourseTitle);
        Equal("示例小节", s.LessonText);
        Equal(12, s.Progress);
        Equal(76, s.Done);
        Equal(106, s.Total);

        File.AppendAllText(file, "播放状态 | 已学=3.1min 平台进度=15% 播放器=322s 总时长=1140s", Utf8);
        Equal(12, tail.Poll(dir, true).Progress);
        File.AppendAllText(file, "\n", Utf8);
        s = tail.Poll(dir, true);
        Equal(15, s.Progress);
        Equal(322, s.PlayerSeconds);
        Equal(1140, s.DurationSeconds);

        File.AppendAllText(file, "课时完成 | 平台进度=100% 进度确认=True\n" +
            "课时结果 | 序号=1/30 标题=示例小节 平台进度=100% 本课用时=122s\n" +
            "课时结果 | 序号=1/30 标题=示例小节 完成=True 平台进度=100%\n" +
            "课时开始 | 序号=2/30 标题=15.6 下一节 平台进度=0%\n", Utf8);
        s = tail.Poll(dir, true);
        Equal(77, s.Done);
        Equal(106, s.Total);
        Equal("15.6 下一节", s.LessonText);
        Equal(0, s.Progress);
        Equal(-1, s.PlayerSeconds);

        File.AppendAllText(file, "课时结果 | 序号=2/30 标题=下一节 完成=False 平台进度=27%\n" +
            "课程目录 | 类型=wisdom\n", Utf8);
        s = tail.Poll(dir, true);
        Equal("—", s.LessonText);
        Equal("", s.CourseTitle);
        Equal(-1, s.Progress);
        Equal(-1, s.Done);
        Equal(0, s.Total);

        File.AppendAllText(file, "课时开始 | 序号=1/5 标题=缺少目录统计 平台进度=42%\n", Utf8);
        s = tail.Poll(dir, true);
        Equal(42, s.Progress);
        Equal(-1, s.Done);
        Equal(0, s.Total);

        File.AppendAllText(file, "课程目录 | 类型=fusion\nGet to-learn class: 0 / 4\n" +
            "课时开始 | 序号=1/4 标题=复习小节 平台进度=100%\n" +
            "课时结果 | 序号=1/4 标题=复习小节 完成=True\n", Utf8);
        s = tail.Poll(dir, true);
        Equal(4, s.Done);
        Equal(4, s.Total);
        Equal(100, s.Progress);

        File.Delete(file);
        TestIdleWhileStopped(dir, file);
        TestRunSilence(dir, file);
        TestQuickSettings(dir);
        TestOverview();
        Directory.Delete(dir);
        Console.WriteLine("Progress tests passed.");
    }

    private static void TestIdleWhileStopped(string dir, string file)
    {
        File.WriteAllText(file, "检测到安全验证\n", Utf8);
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-25));
        StudySnapshot stopped = new LogTail().Poll(dir, false);
        Equal(-1.0, stopped.IdleMinutes);
        Equal(false, stopped.NeedAttention);
        File.Delete(file);
    }

    private static void TestRunSilence(string dir, string file)
    {
        DateTime now = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
        LogTail tail = new LogTail(() => now);
        File.WriteAllText(file, "检测到安全验证\n", Utf8);
        File.SetLastWriteTimeUtc(file, now.AddMinutes(-40));

        tail.StartRun(now);
        StudySnapshot started = tail.Poll(dir, true);
        Equal(0.0, started.IdleMinutes);
        Equal(false, started.NeedAttention);

        now = now.AddMinutes(9);
        Equal(false, tail.Poll(dir, true).NeedAttention);
        now = now.AddMinutes(1);
        StudySnapshot stale = tail.Poll(dir, true);
        Equal(10.0, stale.IdleMinutes);
        Equal(true, stale.NeedAttention);
        Equal(true, stale.AttentionText.Contains("可能暂停或等待验证"));

        File.AppendAllText(file, "检测到安全验证\n", Utf8);
        File.SetLastWriteTimeUtc(file, now);
        StudySnapshot verification = tail.Poll(dir, true);
        Equal(0.0, verification.IdleMinutes);
        Equal("检测到安全验证，需要人工处理", verification.AttentionText);

        now = now.AddMinutes(1);
        File.AppendAllText(file, "安全验证已完成\n", Utf8);
        File.SetLastWriteTimeUtc(file, now);
        StudySnapshot recovered = tail.Poll(dir, true);
        Equal(0.0, recovered.IdleMinutes);
        Equal(false, recovered.NeedAttention);

        now = now.AddMinutes(2);
        Equal(2.0, tail.Poll(dir, true).IdleMinutes);
        Equal(-1.0, tail.Poll(dir, false).IdleMinutes);
        Equal(false, tail.Poll(dir, false).NeedAttention);
        Equal(0.0, tail.Poll(dir, true).IdleMinutes);

        File.Delete(file);
        now = now.AddMinutes(3);
        tail.StartRun(now); // Restart between polls, with no log from the new run yet.
        now = now.AddMinutes(2);
        Equal(2.0, tail.Poll(dir, true).IdleMinutes);
        Equal(-1.0, tail.Poll(dir, false).IdleMinutes);
    }

    private static void TestQuickSettings(string dir)
    {
        string path = Path.Combine(dir, "config.ini");
        File.WriteAllLines(path, new[] {
            "[user-account]", "username = sample", "; keep this comment",
            "[course-option]", "soundOff = True", "limitSpeed = 1.6",
            "[panel]", "splitPos = 640", "[extra]", "keep = unchanged"
        }, Utf8);
        Config.SaveSwitch(path, "panel", "gamePause", false);
        Config.SaveSwitch(path, "course-option", "soundOff", false);
        Config config = Config.Load(path);
        Equal(false, config.GamePause);
        Equal(false, config.SoundOff);
        Equal(1.6, config.LimitSpeed);
        Equal("sample", config.Username);
        string text = File.ReadAllText(path, Utf8);
        Equal(true, text.Contains("; keep this comment"));
        Equal(true, text.Contains("keep = unchanged"));
        File.Delete(path);
    }

    private static void TestOverview()
    {
        StudySnapshot sample = new StudySnapshot {
            Running = true, Total = 106, Done = 82, LessonText = "公共服务", Progress = 41,
            Catalog = new CourseCatalog {
                Title = "形势与政策",
                Lessons = new[] {
                    new CatalogLesson { Chapter = "社会建设", Number = "15.4", Title = "社会治理", Progress = 100 },
                    new CatalogLesson { Chapter = "社会建设", Number = "15.5", Title = "公共服务", Progress = 41, Current = true },
                    new CatalogLesson { Chapter = "社会建设", Number = "15.6", Title = "民生发展" },
                    new CatalogLesson { Chapter = "社会建设", Number = "16.1", Title = "共同发展", Progress = 100 }
                }
            }
        };
        LearningOverview view = LearningOverview.From(sample);
        Equal("形势与政策", view.CourseTitle);
        Equal("第 15 章 · 社会建设", view.ChapterTitle);
        Equal("15.5", view.LessonNumber);
        Equal("公共服务", view.LessonTitle);
        Equal(82, view.Course.Done);
        Equal(106, view.Course.Total);
        Equal(77, view.Course.Percent);
        Equal(1, view.Chapter.Done);
        Equal(3, view.Chapter.Total);
        Equal(33, view.Chapter.Percent);

        sample.Total = 4;
        view = LearningOverview.From(sample);
        Equal(2, view.Course.Done);
        Equal(50, view.Course.Percent);
        sample.Total = 0;
        Equal(false, LearningOverview.From(sample).Course.Known);

        sample.Total = 106;
        sample.Catalog = null;
        sample.CourseTitle = "日志课程";
        view = LearningOverview.From(sample);
        Equal("日志课程", view.CourseTitle);
        Equal("公共服务", view.LessonTitle);
        Equal(77, view.Course.Percent);
        Equal(false, view.Chapter.Known);
    }
}
