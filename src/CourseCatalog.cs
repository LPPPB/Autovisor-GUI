using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace AutovisorPanel
{
    internal sealed class CourseCatalog
    {
        public CourseCatalog() { }
        public string Title { get; set; }
        public CatalogLesson[] Lessons { get; set; }
    }

    internal sealed class CatalogLesson
    {
        public CatalogLesson() { Progress = -1; }
        public string Chapter { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public int Progress { get; set; }
        public bool Current { get; set; }
    }

    internal struct CompletionCount
    {
        public readonly int Done;
        public readonly int Total;

        public CompletionCount(int done, int total)
        {
            Done = done;
            Total = total;
        }

        public bool Known { get { return Total > 0 && Done >= 0 && Done <= Total; } }
        public int Percent { get { return Known ? (int)(Done * 100L / Total) : 0; } }
    }

    internal sealed class LearningOverview
    {
        private static readonly Regex ChapterHeading = new Regex(@"^第\s*[0-9一二三四五六七八九十百千零〇两]+\s*章");

        public string CourseTitle;
        public string ChapterTitle;
        public string LessonTitle;
        public string LessonNumber;
        public CompletionCount Course;
        public CompletionCount Chapter;

        public static LearningOverview From(StudySnapshot snapshot)
        {
            CourseCatalog catalog = snapshot.Catalog;
            CatalogLesson[] lessons = catalog == null || catalog.Lessons == null ? new CatalogLesson[0] : catalog.Lessons;
            LearningOverview result = new LearningOverview
            {
                CourseTitle = catalog != null && !string.IsNullOrWhiteSpace(catalog.Title) ? catalog.Title : snapshot.CourseTitle,
                LessonTitle = snapshot.LessonText,
                Course = new CompletionCount(snapshot.Done, snapshot.Total)
            };

            // Use directory totals only when they agree with the complete course size from the log.
            if (snapshot.Total > 0 && lessons.Length == snapshot.Total && lessons.All(lesson => lesson != null))
                result.Course = new CompletionCount(lessons.Count(lesson => lesson.Progress >= 100), snapshot.Total);

            CatalogLesson current = snapshot.Running ? lessons.FirstOrDefault(lesson => lesson != null && lesson.Current) : null;
            if (current == null && !string.IsNullOrWhiteSpace(snapshot.LessonText) && snapshot.LessonText != "—")
            {
                CatalogLesson[] matches = lessons.Where(lesson => MatchesLesson(lesson, snapshot.LessonText)).Take(2).ToArray();
                if (matches.Length == 1) current = matches[0];
            }
            if (current == null) return result;

            result.LessonNumber = current.Number;
            result.LessonTitle = current.Title;
            string number = ChapterNumber(current.Number);
            string name = HasChapter(current) ? current.Chapter.Trim() : "";
            if (name.Length > 0 && ChapterHeading.IsMatch(name)) result.ChapterTitle = name;
            else if (number.Length > 0)
                result.ChapterTitle = "第 " + number + " 章" + (name.Length > 0 ? " · " + name : "");
            else result.ChapterTitle = name;

            if (name.Length > 0 || number.Length > 0)
            {
                CatalogLesson[] chapter = lessons.Where(lesson => InChapter(lesson, current, number)).ToArray();
                result.Chapter = new CompletionCount(chapter.Count(lesson => lesson.Progress >= 100), chapter.Length);
            }
            return result;
        }

        private static bool MatchesLesson(CatalogLesson lesson, string text)
        {
            return lesson != null && (lesson.Title == text ||
                (!string.IsNullOrEmpty(lesson.Number) && lesson.Number + " " + lesson.Title == text));
        }

        private static bool HasChapter(CatalogLesson lesson)
        {
            return !string.IsNullOrWhiteSpace(lesson.Chapter) && lesson.Chapter.Trim() != "课程小节";
        }

        private static string ChapterNumber(string number)
        {
            if (string.IsNullOrWhiteSpace(number)) return "";
            int dot = number.IndexOfAny(new[] { '.', '．' });
            return dot > 0 ? number.Substring(0, dot) : "";
        }

        private static bool InChapter(CatalogLesson lesson, CatalogLesson current, string number)
        {
            if (lesson == null) return false;
            string otherNumber = ChapterNumber(lesson.Number);
            if (number.Length > 0 && otherNumber.Length > 0) return number == otherNumber;
            if (HasChapter(current))
                return HasChapter(lesson) && lesson.Chapter.Trim() == current.Chapter.Trim();
            return false;
        }
    }

    internal sealed class CatalogCapture
    {
        private const int MaxBytes = 8 * 1024 * 1024;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = MaxBytes };
        private string scriptPath;
        private string snapshotPath;
        private DateTime lastWrite;
        private CourseCatalog last;

        public void Prepare(ProcessStartInfo start, string runtimeDir)
        {
            Close();
            last = null;
            lastWrite = DateTime.MinValue;
            if (!File.Exists(Path.Combine(runtimeDir, "internal", "playwright", "driver", "node.exe"))) return;
            try
            {
                string prefix = Path.Combine(Path.GetTempPath(), "autovisor-catalog-" + Guid.NewGuid().ToString("N"));
                scriptPath = prefix + ".cjs";
                snapshotPath = prefix + ".json";
                using (Stream resource = typeof(CatalogCapture).Assembly.GetManifestResourceStream("AutovisorPanel.CatalogCapture"))
                using (FileStream script = File.Open(scriptPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    resource.CopyTo(script);

                string previousOptions = Environment.GetEnvironmentVariable("NODE_OPTIONS") ?? "";
                start.UseShellExecute = false;
                start.EnvironmentVariables["AUTOVISOR_PANEL_CATALOG"] = snapshotPath;
                start.EnvironmentVariables["NODE_OPTIONS"] =
                    (previousOptions + " --require=\"" + scriptPath.Replace('\\', '/') + "\"").Trim();
            }
            catch (IOException) { Close(); }
            catch (UnauthorizedAccessException) { Close(); }
        }

        public CourseCatalog Read()
        {
            if (snapshotPath == null) return last;
            try
            {
                FileInfo info = new FileInfo(snapshotPath);
                if (!info.Exists || info.LastWriteTimeUtc == lastWrite || info.Length > MaxBytes) return last;
                using (FileStream stream = new FileStream(snapshotPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(stream))
                {
                    string data = reader.ReadToEnd();
                    last = data == "null" ? null : json.Deserialize<CourseCatalog>(data);
                    lastWrite = info.LastWriteTimeUtc;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return last;
        }

        public void Close()
        {
            if (scriptPath != null) TryDelete(scriptPath);
            if (snapshotPath != null)
            {
                TryDelete(snapshotPath);
                TryDelete(snapshotPath + ".tmp");
            }
            scriptPath = null;
            snapshotPath = null;
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
