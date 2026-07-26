# 평문 클라우드 동기화 검수 안내

## 현재 준비 상태

- 작업 폴더의 `Sentory.exe`는 `1.4.3+developers` 평문 v2 검수판이다.
- 동기화 폴더는 `C:\Users\awds4\Documents\Sentory-googleDrive`다.
- 현재 Google Drive에는 사진 21개와 링크 16개가 올라가 있다.
- 기존 `Sentory Sync/v1`의 59개 파일은 이전 전후 바이트가 같게 보존돼 있다.
- 이전 전 로컬 설정과 SQLite 백업은
  `artifacts/developer-readable-cloud-storage/pre-migration-backup`에 있다.

## 1. Windows에서 기존 자료 확인

1. 작업 폴더의 `Sentory.exe`가 실행 중인지 확인한다.
2. 파일 탐색기에서 `C:\Users\awds4\Documents\Sentory-googleDrive`를 연다.
3. `Photos`에서 사진을 열어 정상 이미지인지 확인한다.
4. `Links/2026/07`에서 TXT 파일을 열어 주소·도메인·저장 시각·출처가
   보이는지 확인한다.
5. `Sentory Sync/v1`은 이전 형식 보존본이므로 직접 수정하거나 지우지 않는다.

## 2. 새 사진과 링크의 즉시 저장 확인

1. Discord 또는 카카오톡에서 이전에 저장하지 않은 사진 한 장을 실제로
   전송한다.
2. Sentory 보관함에 카드가 나타나는지 확인한다.
3. 잠시 뒤 `Photos`에 PNG·JPG 같은 일반 사진 파일이 하나 생기는지 확인한다.
4. 이전에 저장하지 않은 링크를 실제로 전송한다.
5. Sentory 카드와 `Links/<연도>/<월>`의 새 TXT가 생기는지 확인한다.
6. 같은 사진을 다시 전송했을 때 `Photos`의 같은 사진 파일이 중복 생성되지
   않는지 확인한다.

Sentory는 로컬 클라우드 폴더까지는 즉시 기록하지만, Google 서버 업로드 시간은
Drive 데스크톱 앱의 네트워크 상태에 따라 몇 초 더 걸릴 수 있다.

## 3. Google Drive 웹에서 확인

1. `drive.google.com`에 현재 PC와 같은 계정으로 로그인한다.
2. 왼쪽의 `컴퓨터`를 연다.
3. `내 컴퓨터/Sentory-googleDrive`로 이동한다.
4. `Photos`의 사진을 열어 미리보기가 나오는지 확인한다.
5. 사진 화면의 `다운로드`와 `공유` 버튼이 활성화되는지 확인한다.
6. `Links/<연도>/<월>`의 TXT를 열어 URL 본문이 보이는지 확인한다.
7. 링크 TXT에도 `다운로드`와 `공유` 버튼이 있는지 확인한다.

이 폴더는 Google Drive의 `내 드라이브`가 아니라 `컴퓨터` 아래에 있다.

## 4. Android에서 확인

1. Google Drive 앱을 설치하고 같은 계정으로 로그인한다.
2. 아래쪽 `파일`을 누른 뒤 `컴퓨터` 탭을 연다.
3. `내 컴퓨터/Sentory-googleDrive/Photos`로 이동한다.
4. 사진 미리보기, 기기 다운로드 또는 오프라인 저장, Android 공유 메뉴 전송을
   각각 한 번 확인한다.
5. `Links/<연도>/<월>`로 이동해 TXT 본문에서 URL을 확인한다.
6. TXT 파일 자체 공유와 URL 텍스트 복사를 각각 확인한다.

[Google 공식 Android 안내](https://support.google.com/drive/answer/2424384?co=GENIE.Platform%3DAndroid&hl=ko)도
Drive 데스크톱에서 동기화한 파일을 `파일/컴퓨터` 탭에서 찾도록 설명한다.
메뉴 이름은 앱 버전과 언어에 따라 조금 다를 수 있다.

## 5. iPhone 또는 iPad에서 확인

1. Google Drive 앱을 설치하고 같은 계정으로 로그인한다.
2. `파일` 영역에서 `컴퓨터` 또는 `내 컴퓨터`를 찾아
   `Sentory-googleDrive`를 연다.
3. 사진 미리보기·다운로드·공유 시트를 확인한다.
4. 링크 TXT 본문 열기·파일 공유·URL 복사를 확인한다.
5. 앱에서 `컴퓨터` 위치가 보이지 않으면 Safari에서 `drive.google.com`의
   데스크톱 웹 보기를 사용해 같은 경로를 먼저 확인하고 문제로 기록한다.

## 6. 다른 Windows 또는 macOS 컴퓨터 연결

1. 같은 Google 계정으로 Drive 데스크톱 앱에 로그인한다.
2. Google Drive의 `컴퓨터/내 컴퓨터/Sentory-googleDrive`를 다른 컴퓨터의
   로컬 폴더로 동기화한다.
3. 그 컴퓨터에 같은 Sentory 검수판을 놓고 설정의 `컴퓨터 간 동기화`에서 해당
   로컬 폴더를 선택한다.
4. 동기화를 켠 뒤 기존 사진·링크 카드가 한 번씩 나타나는지 확인한다.
5. 두 컴퓨터에서 각자 새 사진과 링크를 하나씩 보내 반대쪽에 나타나는지
   확인한다.

macOS용 Sentory 앱 자체는 아직 없으므로 현재 macOS에서는 Drive/Finder로
파일 보기·다운로드·공유만 가능하다. Sentory 보관함 송수신 검수는 Windows 두
대 또는 추후 macOS 앱이 준비된 뒤 진행한다.

## 7. 두 Windows 보관함의 삭제 동기화 확인

1. 두 Sentory에서 같은 동기화 폴더를 선택하고 기존 사진·링크가 양쪽에 보이는
   상태를 만든다.
2. 메인 컴퓨터에서 검수용 사진 하나를 삭제한다.
3. Sentory의 최근 동기화가 완료된 뒤 클라우드 `Photos`에서 같은 사진이
   없어지고 VM 보관함에서도 사라지는지 확인한다.
4. VM에서 다른 검수용 사진이나 링크를 삭제한다.
5. 30초 이상 기다려도 VM에 다시 생기지 않고, 메인 컴퓨터와 클라우드의
   평문 파일에서도 없어지는지 확인한다.
6. 삭제 전에 이미 실행한 구버전에서 지운 항목은 삭제 기록이 없으므로 자동
   추론할 수 없다. 새 검수판을 양쪽에 적용한 뒤 남아 있는 쪽에서 한 번 더
   삭제해 검증한다.

## 문제를 알려줄 때 필요한 정보

- 어느 단계에서 문제가 났는지
- Windows, Android, iPhone/iPad, Google Drive 웹 중 어느 환경인지
- 사진인지 링크인지
- Sentory 설정에 보이는 최근 동기화 상태
- 가능하면 문제가 보이는 화면 캡처

사진이나 링크 내용 자체가 민감하면 화면 캡처에서 가리고 알려줘도 된다.
