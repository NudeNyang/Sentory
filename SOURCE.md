# Sentory 소스 코드 안내

Sentory의 원본 소스 코드는 다음 저장소에서 공개합니다.

- 저장소: https://github.com/NudeNyang/Sentory
- 라이선스: GNU General Public License v3.0 only (`GPL-3.0-only`)

각 Release의 `Sentory-<버전>-source.zip`과 같은 버전의 Git 태그는 해당
버전의 실행 파일을 만든 소스와 대응합니다. 1.0.1 배포판의 소스는
`Sentory-1.0.1-source.zip` 또는 `v1.0.1` 태그에서 받을 수 있습니다.

## 빌드

Windows 10/11과 .NET 8 SDK가 필요합니다.

```powershell
git clone https://github.com/NudeNyang/Sentory.git
cd Sentory
dotnet restore .\Sentory.sln
dotnet build .\Sentory.sln --configuration Release
dotnet test .\Sentory.sln --configuration Release
```

설치형과 포터블 배포 파일까지 만들려면 Inno Setup 6을 설치한 뒤 다음 명령을
실행합니다.

```powershell
.\scripts\Publish-Release.ps1 -Version 1.0.1
```

## 배포할 때 지켜야 할 점

GPL 조건에 따라 Sentory를 수정하고 배포할 수 있으며 상업적으로 이용할 수도
있습니다. 바이너리나 수정본을 다른 사람에게 제공할 때는 그 배포본에 대응하는
소스 코드와 GNU GPL v3 라이선스 고지를 함께 제공해야 합니다. 자세한 조건은
`LICENSE.txt` 또는 `COPYING`에서 확인할 수 있습니다.

Sentory에 포함된 .NET, Microsoft.Data.Sqlite, SQLitePCLRaw와 SQLite는 각
구성 요소의 라이선스를 따릅니다. 자세한 내용은
`THIRD-PARTY-NOTICES.txt`를 확인해 주세요.

---

# Sentory source code

The original Sentory source code is published at:

- Repository: https://github.com/NudeNyang/Sentory
- License: GNU General Public License v3.0 only (`GPL-3.0-only`)

The `Sentory-<version>-source.zip` archive and matching Git tag correspond to the
binaries in each Release. Source for version 1.0.1 is available from
`Sentory-1.0.1-source.zip` or the `v1.0.1` tag.

You may use, modify, redistribute, and commercially use Sentory under the GPL.
When distributing binaries or modified versions, provide the corresponding source
code and GPL notices as required by the license. See `LICENSE.txt` or `COPYING`
for the complete terms. Third-party components remain under their respective
licenses as listed in `THIRD-PARTY-NOTICES.txt`.
