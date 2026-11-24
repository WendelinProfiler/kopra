namespace Kopra;

static class Regression
{
    public static int Run()
    {
        L.Use("de");
        string root = Path.Combine(Path.GetTempPath(), "Kopra-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int cases = 0;
        void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception(name);
            cases++;
        }
        try
        {
            var state = new WinKeyState();
            Check(state.Handle(0x5B, true).swallow, "Windows down must wait");
            var solo = state.Handle(0x5B, false);
            Check(solo.swallow && solo.open, "Windows alone opens search");
            foreach (uint key in new uint[] { 0x45, 0x4C, 0x44, 0x52, 0x09, 0x20, 0x31, 0x25 })
            {
                state = new();
                Check(state.Handle(0x5B, true).swallow, "Chord starts pending");
                var chord = state.Handle(key, true);
                Check(!chord.swallow && chord.replay && !chord.open, "Windows chord must replay Windows before second key");
                Check(!state.Handle(key, true).replay, "Repeat must not replay Windows twice");
                var up = state.Handle(0x5B, false);
                Check(!up.swallow && !up.open, "Windows chord up passes through");
            }
            state = new();
            Check(!state.Handle(0x5B, true, true).swallow, "Existing Ctrl/Alt/Shift modifiers pass Windows through");
            using (var store = new HistoryStore(root))
            {
                store.AddText("O'Brien \"Unicode\" ä ö 中文\nsecond line");
                store.AddText("O'Brien \"Unicode\" ä ö 中文\nsecond line");
                Check(store.All().Count == 1, "Text duplicates collapse in SQLite");
                Check(store.All()[0].Text.Contains("中文"), "SQLite persists Unicode and quotes");
                string original = Path.Combine(root, "source");
                Directory.CreateDirectory(original);
                File.WriteAllText(Path.Combine(original, "one.txt"), "independent file contents");
                Directory.CreateDirectory(Path.Combine(original, "empty"));
                var file = store.AddFiles([original], 1024).GetAwaiter().GetResult();
                Directory.Delete(original, true);
                Check(File.ReadAllText(Path.Combine(file.Files[0], "one.txt")) == "independent file contents", "History survives deleted original folder");
                Check(Directory.Exists(Path.Combine(file.Files[0], "empty")), "Empty folders preserved");
                string large = Path.Combine(root, "large.bin");
                File.WriteAllBytes(large, new byte[2048]);
                bool refused = false;
                try
                {
                    store.AddFiles([large], 1024).GetAwaiter().GetResult();
                }
                catch (IOException) { refused = true; }
                Check(refused, "Entry size bound enforced");
                Check(Directory.GetDirectories(Path.Combine(root, "objects")).Length == 1, "Failed copies leave no partial object");
                store.Pin(file);
                store.Prune(new Preferences { StorageLimitMb = 1, RetentionDays = 30 });
                Check(store.All().Any(x => x.Id == file.Id && x.Pinned), "Pinned file survives cleanup");
                store.Clear();
                Check(store.All().Count == 1 && File.Exists(Path.Combine(file.Files[0], "one.txt")), "Clear preserves pinned file and data");
                store.Clear(true);
                Check(store.All().Count == 0 && !Directory.Exists(file.Files[0]), "Explicit clear deletes owned backing files");
                store.AddText("persisted");
                store.Use("test-app");
                Check(store.Usage("test-app") == 1, "App use recorded");
            }
            using (var reopened = new HistoryStore(root))
            {
                Check(reopened.All().Single().Text == "persisted", "History survives process-style reopen");
                Check(reopened.Usage("test-app") == 1, "Ranking survives reopen");
            }
            Check(SearchService.Rank("Explorer", "explorer") > SearchService.Rank("Explorer extras", "explorer"), "Exact search outranks prefix");
            Check(SearchService.Rank("Sound Einstellungen", "sound ein") > 0, "Multiword search");
            Check(SearchService.Rank("Bluetooth", "xyz") == 0, "Unrelated result excluded");
            var prefs = new Preferences { Paused = true, SearchFolders = ["sample"], StorageLimitMb = 256 };
            prefs.Save(root);
            Check(Preferences.Load(root).Paused && Preferences.Load(root).StorageLimitMb == 256, "Preferences round trip");
            Check(FileSafety.IsSupportedTag(0x9000001A), "Cloud folders are not treated as symbolic links");
            Check(!FileSafety.IsSupportedTag(0xA000000C) && !FileSafety.IsSupportedTag(0xA0000003), "Symbolic links and junctions remain guarded");
            string folderForDrop = Path.Combine(root, "drop-folder");
            Directory.CreateDirectory(folderForDrop);
            File.WriteAllText(Path.Combine(folderForDrop, "child.txt"), "folder contents");
            var data = new DataObject();
            var collection = new System.Collections.Specialized.StringCollection();
            collection.Add(folderForDrop);
            data.SetFileDropList(collection);
            var drop = ClipboardService.ReadFiles(data);
            Check(drop is { Length: 1 } && Directory.Exists(drop[0]), "Explorer-style folder drop is accepted");
            using (var store = new HistoryStore(Path.Combine(root, "expanded")))
            {
                var folderEntry = store.AddFiles(drop!, 4096).GetAwaiter().GetResult();
                Check(Directory.Exists(folderEntry.Files[0]) && File.Exists(Path.Combine(folderEntry.Files[0], "child.txt")), "Folder drop produces a usable history entry");
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(80, 60, 96, 96, PixelFormats.Pbgra32);
                var visual = new DrawingVisual();
                using (var draw = visual.RenderOpen())
                    draw.DrawRectangle(Brushes.DodgerBlue, null, new Rect(0, 0, 80, 60));
                bitmap.Render(visual);
                var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                string image = Path.Combine(root, "actual-picture.jpeg");
                using (var output = File.Create(image))
                    encoder.Save(output);
                var picture = store.AddFiles([image], 65536).GetAwaiter().GetResult();
                File.Delete(image);
                Check(LauncherWindow.Thumbnail(picture) is System.Windows.Media.Imaging.BitmapSource preview && preview.PixelWidth > 0, "Copied JPEG renders a real picture after original deletion");
                store.RecordRecent(new("Recent file", picture.Files[0], "Datei", "test"));
                Check(store.Recent().Single().Item.Title == "Recent file", "Recent file is persisted");
                store.RecordRecent(new("Recent app", "test.exe", "App", "test"));
                Check(store.Recent().First().Item.Title == "Recent app", "Suggestions sort by last use, not alphabet");
            }
            cases += StorageRegression.Run(root);
            cases += SyncRegression.Run(root);
            cases += PairingRegression.Run();
            cases += LocalizationRegression.Run(root);
            File.WriteAllText(Path.Combine(root, "test-result.txt"), $"PASS {cases} cases");
            Console.WriteLine($"PASS {cases} cases");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), $"PASS {cases} cases");
            return 0;
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), "FAIL " + e); return 1; }
        finally { if (Directory.Exists(root) && Path.GetFileName(root).StartsWith("Kopra-tests-", StringComparison.Ordinal)) Directory.Delete(root, true); }
    }
}
