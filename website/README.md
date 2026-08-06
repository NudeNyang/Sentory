# Sentory 랜딩페이지

Show GN과 검색 유입에서 Sentory를 소개하기 위한 정적 랜딩페이지다. 별도 빌드나
서버 런타임 없이 GitHub Pages에 올릴 수 있다.

## 로컬 확인

```powershell
cd website
python -m http.server 4173
```

브라우저에서 `http://127.0.0.1:4173`을 연다.

## 테스트

```powershell
cd website
npm test
```

## 배포

저장소의 `Deploy Sentory landing page` Actions 워크플로를 수동 실행한다. 최초
배포 전 GitHub 저장소의 Pages 설정에서 Source를 `GitHub Actions`로 선택해야 한다.

현재 canonical URL과 sitemap은 `https://nudenyang.github.io/Sentory/`를 기준으로
한다. 사용자 도메인을 연결하면 `index.html`, `robots.txt`, `sitemap.xml`의 URL도
함께 바꾼다.
