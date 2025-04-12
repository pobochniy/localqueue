using System.Runtime.CompilerServices;

// Decided to not make internals visible to Dodo.LocalCommandQueue.Tests because at least it possible to make
// integration tests less end to end

[assembly: InternalsVisibleTo("LocalQueue.Tests")]