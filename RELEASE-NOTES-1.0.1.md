# Sentory 1.0.1

Sentory 1.0.1은 사진 드래그 감지 안정성을 보완하고 배포 라이선스를 GNU GPL
v3로 변경한 업데이트입니다.

## 달라진 점

- Discord와 카카오톡에서 탐색기 사진을 빠르게 드래그할 때 일부 파일을 놓치던
  문제를 수정했습니다.
- 정상적인 파일 드롭을 마지막 커서 위치 검사에서 잘못 취소하던 문제를
  수정했습니다.
- Sentory의 원본 소스를 GNU GPL v3 전용(`GPL-3.0-only`)으로 공개합니다.
- 실행 파일과 정확히 같은 커밋의 소스 ZIP 및 SHA-256 확인값을 Release에
  포함합니다.
- 앱, 설치 프로그램과 문서의 버전 및 라이선스 안내를 1.0.1 기준으로
  정리했습니다.

## 다운로드

대부분의 Windows PC에서는 `Sentory-win-x64-setup.exe`를 받으시면 됩니다.
설치하지 않고 사용하려면 `Sentory-win-x64-portable.zip`을 완전히 푼 뒤
`Sentory.exe`를 실행해 주세요. Windows on ARM 사용자는 이름에 `arm64`가
포함된 파일을 선택해 주세요.

현재 파일에는 코드 서명이 적용되지 않았습니다. Windows에서 알 수 없는 게시자
경고가 나타날 수 있으므로 이 저장소의 공식 Release인지 확인하고, 필요한 경우
함께 제공되는 `.sha256` 파일로 확인값을 비교해 주세요.

## 소스 코드와 라이선스

이 Release의 실행 파일에 대응하는 소스는 `Sentory-1.0.1-source.zip`과
`v1.0.1` 태그에서 받을 수 있습니다. Sentory는 GNU General Public License
v3.0 only로 배포합니다. 자세한 내용은 `LICENSE.txt`, `COPYING`과
`SOURCE.md`를 확인해 주세요.

## 알려진 제한 사항

- Discord 또는 카카오톡의 화면 구조가 바뀌면 감지가 일시적으로 동작하지 않을
  수 있습니다.
- ARM64 패키지는 교차 빌드와 실행 파일 구조 검증을 마쳤지만 실제 Windows on
  ARM 장치에서의 최종 검수는 남아 있습니다.

---

## English

Sentory 1.0.1 improves Explorer drag-and-drop detection for Discord and KakaoTalk
and changes the project license to GNU GPL v3.

### Changes

- Fixed intermittent misses when local images were dragged quickly into Discord or
  KakaoTalk.
- Fixed a final cursor-position check that could cancel a valid file drop.
- Published Sentory's original source under GPL-3.0-only.
- Added a source archive and SHA-256 checksum that correspond to the exact release
  commit.
- Updated application, installer, and documentation version and license notices.

Most Windows PCs should use `Sentory-win-x64-setup.exe`. Use the portable ZIP if
you prefer not to install the app, or an `arm64` package on Windows on ARM.
The binaries are not code-signed, so Windows may show an Unknown Publisher warning.

The corresponding source is available as `Sentory-1.0.1-source.zip` and from the
`v1.0.1` tag. See `LICENSE.txt`, `COPYING`, and `SOURCE.md` for details.
