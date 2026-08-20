# Third-party notices

## 7-Zip 26.02

Extract & Delete distributes the official 7-Zip 26.02 x64 command-line
engine (`7z.exe` and `7z.dll`) under `ThirdParty/7-Zip` in application
outputs. 7-Zip is distributed under the licenses and notices included in:

- `third_party/7zip/26.02/licenses/License.txt`
- `third_party/7zip/26.02/licenses/copying.txt`
- `third_party/7zip/26.02/licenses/unRarLicense.txt`

The official source and release information are available at:

https://github.com/ip7z/7zip/releases/tag/26.02

The application does not load a system installation of 7-Zip, search `PATH`,
or use `7z.dll` through P/Invoke. It starts the pinned executable using a
fixed package-relative path and verifies the recorded SHA-256 values before
use.
