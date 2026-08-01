namespace PhiAgent.Tests;

public class SessionStorageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public SessionStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "phi-session-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "session.jsonl");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private static UserSessionEntry UserEntry(string content) => new(0, content);
    private static AssistantSessionEntry AssistantEntry(params ContentBlock[] blocks) =>
        new(0, blocks, StopReasons.Stop, new Usage());
    private static ToolResultSessionEntry ToolResultEntry(string toolCallId) =>
        new(0, toolCallId, "bash", [new TextBlock("ok")], IsError: false);

    [Test]
    public async Task Append_NewFile_CreatesParentDirectory()
    {
        var nested = Path.Combine(_tempDir, "nested", "deeper", "session.jsonl");
        var storage = new SessionStorage(nested);

        storage.Append(UserEntry("hi"));

        await Assert.That(File.Exists(nested)).IsTrue();
    }

    [Test]
    public async Task ReadAll_EmptyOrMissingFile_ReturnsEmptyList()
    {
        var freshPath = Path.Combine(_tempDir, "does-not-exist.jsonl");
        var storage = new SessionStorage(freshPath);

        await Assert.That(storage.ReadAll()).IsEmpty();
    }

    [Test]
    public async Task Append_ThreeEntries_ThenReadAll_PreservesOrder()
    {
        var storage = new SessionStorage(_filePath);
        storage.Append(UserEntry("first"));
        storage.Append(AssistantEntry(new TextBlock("ack")));
        storage.Append(ToolResultEntry("c1"));

        var entries = storage.ReadAll();

        await Assert.That(entries.Count).IsEqualTo(3);
        await Assert.That(entries[0]).IsTypeOf<UserSessionEntry>();
        await Assert.That(((UserSessionEntry)entries[0]).Content).IsEqualTo("first");
        await Assert.That(entries[1]).IsTypeOf<AssistantSessionEntry>();
        await Assert.That(entries[2]).IsTypeOf<ToolResultSessionEntry>();
        await Assert.That(((ToolResultSessionEntry)entries[2]).ToolCallId).IsEqualTo("c1");
    }

    [Test]
    public async Task ReadAll_FileWithBlankLines_SkipsThem()
    {
        var storage = new SessionStorage(_filePath);
        storage.Append(UserEntry("a"));
        // Inject a blank line as if an external tool edited the file.
        File.AppendAllText(_filePath, "\n\n");
        storage.Append(UserEntry("b"));

        var entries = storage.ReadAll();

        await Assert.That(entries.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Clear_DeletesFile()
    {
        var storage = new SessionStorage(_filePath);
        storage.Append(UserEntry("x"));
        await Assert.That(File.Exists(_filePath)).IsTrue();

        storage.Clear();

        await Assert.That(File.Exists(_filePath)).IsFalse();
        await Assert.That(storage.ReadAll()).IsEmpty();
    }

    [Test]
    public async Task Clear_OnMissingFile_DoesNotThrow()
    {
        var storage = new SessionStorage(_filePath);

        storage.Clear(); // should not throw
    }

    [Test]
    public async Task Append_FromMultipleThreads_AllEntriesPersisted()
    {
        var storage = new SessionStorage(_filePath);

        var tasks = Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 25; i++)
            {
                storage.Append(UserEntry($"t{t}-m{i}"));
            }
        })).ToList();
        await Task.WhenAll(tasks);

        await Assert.That(storage.ReadAll().Count).IsEqualTo(200);
    }
}
