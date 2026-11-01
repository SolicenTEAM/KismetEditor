# Explicit-path Oodle loader

Derived from `oodle_loader` 0.2.2 in [trumank/repak](https://github.com/trumank/repak/tree/71d06644746134bd59ed1467bae1856704d2f8bb/oodle_loader), commit `71d06644746134bd59ed1467bae1856704d2f8bb`, under its MIT license (included).

The Oodle function signatures and compression API are retained. This local adaptation replaces automatic executable-relative discovery/download with `initialize(absolute_path)` and `is_initialized()`. Failed initialization can be retried. It does not change the current directory, environment, or the pinned retoc submodule. Acquisition and SHA-256-checked extraction are handled by KissE; the loader itself never downloads or writes files.

No Oodle binary is included in this source directory. Embedding/distributing a supplied binary requires the corresponding rights.
