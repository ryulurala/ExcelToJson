# 🚀 Node.js Core (CLI & Builder)

엑셀 파일을 JSON으로 파싱해 주는 이 프로젝트의 핵심 코어 엔진입니다.

## ✨ 주요 기능

- `xlsx` 라이브러리를 활용한 고속 엑셀 파싱
- 앵커 기호(`{}`, `{{}}`, `[{}]`, `{[]}`)를 통한 유연한 JSON 구조화
- `pkg`를 활용한 크로스 플랫폼 바이너리 빌드 지원 (Windows `.exe` / macOS)

## 🛠️ 사용 방법 (Usage)

### 1. 패키지 설치

```bash
npm install
```

### 2. 샘플 생성 테스트

테스트용 엑셀 샘플 데이터를 생성합니다.

```bash
npm run sample
```

### 3. 실행 파일 빌드 (Windows & macOS 바이너리 생성)

독립적으로 사용할 수 있는 실행 파일을 생성합니다.

Windows용 .exe와 macOS용 바이너리가 모두 만들어집니다.

- 빌드된 결과물은 build/ 폴더 내부에 생성됩니다.

```bash
npm run build
```

### 4. 실행 파일 커맨드라인(CLI) 실행 방법

```bash
# 기본 사용법: [실행파일] [엑셀 원본 경로] [JSON 출력 폴더 경로]
build/converter-win.exe "./sample.xlsx" "./data"      # windows
build/converter-macos "./sample.xlsx" "./data"      # mac
```
