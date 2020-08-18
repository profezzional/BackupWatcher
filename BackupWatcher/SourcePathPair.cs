namespace BackupWatcher
{
    public class SourcePathPair
    {
        public string Source { get; set; }
        public string Target { get; set; }

        public SourcePathPair() { }
        public SourcePathPair(string source, string destination)
        {
            Source = source;
            Target = destination;
        }

        public override string ToString()
        {
            return $"\"{Source}\" => \"{Target}\"";
        }
    }
}