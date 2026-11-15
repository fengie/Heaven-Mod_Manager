# Real RAR / 7z fixture provenance

Issue: #791. Decoder under test: the repository-pinned SharpCompress 0.50.4.

The test method `RealArchiveFormatAcceptanceTests` uses actual archives from
[SharpCompress's 0.50.4 tagged test corpus](https://github.com/adamhathcock/sharpcompress/tree/0.50.4/tests/TestArchives/Archives),
not synthetic ZIPs renamed to another format. It obtains the public bytes at test
time without credentials and verifies the complete Git blob SHA-1 (including the
Git blob object header) **before invoking the application decoder**.

| Name | Bytes | Upstream Git blob SHA-1 | Acceptance |
| --- | ---: | --- | --- |
| Rar4.rar | 59,998 | `189b954c16730be8bd06c5e4fc6b0ffe9c9ff87f` | RAR4 inventory + bounded extraction |
| Rar5.solid.rar | 60,046 | `e16a01da81526b480f9d006d73497e1a9656d010` | Solid RAR5 inventory + bounded extraction |
| 7Zip.nonsolid.7z | 58,982 | `907e1e2c24542ad2d276756dc7d0ecb1f2665eca` | Non-solid 7z inventory + bounded extraction |
| 7Zip.solid.7z | 58,959 | `5ac695e59aeff2385e9065cb8841118f1bde06e1` | Solid 7z inventory + bounded extraction |
| Rar.malformed_512byte.rar | 512 | `914693f8840453420463ef96e01af7040c8e3ff8` | Reject malformed archive without publishing extracted output |

Source license: [SharpCompress MIT License](https://github.com/adamhathcock/sharpcompress/blob/0.50.4/LICENSE.txt), copyright 2014 Adam Hathcock.

## Offline test

Set `MHW_REAL_ARCHIVE_FIXTURES_ROOT` to an existing local directory containing
these exact original filenames. The test checks size and source Git blob SHA
before writing or reading them as mod archives. No special install permission,
test credentials, running game, or live manager folder is needed.

Without the variable, tests download the pinned public raw GitHub files through
an HTTPS client capped at 128 KiB per response and a 30-second timeout.
A failed download is a test infrastructure error, not a decoder success.
The test may run in connected CI; offline CI should provide the verified corpus.

These tests prove only representative real-format decoding and safe bounded
extraction. They do **not** establish broad user-mod compatibility, encrypted
archive support, all multipart RAR variants, giant collection performance,
or installed-client acceptance. Those are separate remaining issue #791/#793
acceptance boundaries.
