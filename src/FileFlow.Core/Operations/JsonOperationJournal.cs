using FileFlow.Core.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileFlow.Core.Operations;

public sealed class JsonOperationJournal : IOperationJournal
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    private readonly string _journalPath;
    private readonly object _syncRoot = new();

    public JsonOperationJournal(string applicationDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataRoot);
        if (!Path.IsPathFullyQualified(applicationDataRoot))
            throw new ArgumentException("The application-data root must be an absolute path.", nameof(applicationDataRoot));

        ApplicationDataRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(applicationDataRoot));
        _journalPath = Path.Combine(ApplicationDataRoot, "journal.jsonl");
    }

    public string ApplicationDataRoot { get; }

    public string TrashRoot => Path.Combine(ApplicationDataRoot, "trash");

    public void Append(OperationJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_syncRoot)
        {
            Directory.CreateDirectory(ApplicationDataRoot);
            string json = JsonSerializer.Serialize(entry, SerializerOptions);
            File.AppendAllText(_journalPath, string.Concat(json, Environment.NewLine));
        }
    }

    public IReadOnlyList<OperationJournalEntry> ReadHistory()
    {
        lock (_syncRoot)
        {
            if (!File.Exists(_journalPath))
                return [];

            try
            {
                return File.ReadLines(_journalPath)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(DeserializeEntry)
                    .GroupBy(entry => entry.TransactionId)
                    .Select(group => group.Last())
                    .OrderByDescending(entry => entry.Timestamp)
                    .ThenBy(entry => entry.TransactionId)
                    .ToArray();
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("The operation journal is malformed.", exception);
            }
        }
    }

    public OperationJournalEntry? FindLatest(Guid transactionId)
    {
        return ReadHistory().SingleOrDefault(entry => entry.TransactionId == transactionId);
    }

    private static OperationJournalEntry DeserializeEntry(string json)
    {
        OperationJournalEntry entry = JsonSerializer.Deserialize<OperationJournalEntry>(json, SerializerOptions)
            ?? throw new InvalidDataException("The operation journal contains a null entry.");
        if (entry.TransactionId == Guid.Empty
            || !Enum.IsDefined(entry.Operation)
            || !Enum.IsDefined(entry.Status)
            || string.IsNullOrWhiteSpace(entry.ConnectedRoot)
            || !Path.IsPathFullyQualified(entry.ConnectedRoot)
            || string.IsNullOrWhiteSpace(entry.Source)
            || !Path.IsPathFullyQualified(entry.Source)
            || string.IsNullOrWhiteSpace(entry.Fingerprint))
        {
            throw new InvalidDataException("The operation journal contains an invalid entry.");
        }

        bool deleteShape = entry.Operation == FileOperationKind.Delete
            && entry.Destination is null
            && entry.TrashPath is not null
            && Path.IsPathFullyQualified(entry.TrashPath);
        bool destinationShape = entry.Operation != FileOperationKind.Delete
            && entry.Destination is not null
            && Path.IsPathFullyQualified(entry.Destination)
            && entry.TrashPath is null;
        if (!deleteShape && !destinationShape)
            throw new InvalidDataException("The operation journal entry shape does not match its operation.");

        return entry;
    }
}
