# OCR 모델 출처와 무결성

이 문서는 Sentory 실행 파일에 포함되는 OCR 모델과 문자 사전의 원본, 버전 및
SHA-256을 기록한다. 아래 값은 2026년 7월 22일 저장소에 포함된 파일과 공식
배포본을 대조한 결과다.

모든 PP-OCRv5 모델과 모델 설정은 PaddlePaddle Authors가 Apache License 2.0으로
배포한다. RapidOcrNet 3.0.0과 이 패키지에 포함된 모델 파일도 Apache License
2.0으로 배포된다.

## 포함 파일

| Sentory 리소스 | 크기 | SHA-256 | 원본 |
| --- | ---: | --- | --- |
| `det.onnx` | 4,819,576바이트 | `4D97C44A20D30A81AAD087D6A396B08F786C4635742AFC391F6621F5C6AE78AE` | RapidOcrNet 3.0.0 NuGet 패키지의 `models/v5/ch_PP-OCRv5_mobile_det.onnx` |
| `cls.onnx` | 1,018,508바이트 | `54379AE5174D026780215FC748A7F31910DEE36818E63D49E17DC598ECC82DF7` | RapidOcrNet 3.0.0 NuGet 패키지의 `models/v5/ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx` |
| `korean-rec.onnx` | 13,418,787바이트 | `92F0B7785E64FC9090106A241CF4C1EB97472824558272751B88A2A4476D3A08` | PaddlePaddle `korean_PP-OCRv5_mobile_rec_onnx`의 `inference.onnx` |
| `cjk-rec.onnx` | 16,534,782바이트 | `DA72DC72CA4DC220DF0DFDE68C1DEDC31C58D3E76A25871122E5056227D50092` | PaddlePaddle `PP-OCRv5_mobile_rec_onnx`의 `inference.onnx` |
| `korean-dict.txt` | 47,451바이트 | `A88071C68C01707489BAA79EBE0405B7BEB5CCA229F4FC94CC3EF992328802D7` | PaddlePaddle `korean_PP-OCRv5_mobile_rec` 설정의 `PostProcess.character_dict` 11,945개 항목 |
| `cjk-dict.txt` | 74,012바이트 | `D1979E9F794C464C0D2E0B70A7FE14DD978E9DC644C0E71F14158CDF8342AF1B` | PaddleOCR 저장소의 `ppocr/utils/dict/ppocrv5_dict.txt` |

## 고정된 원본

### RapidOcrNet 감지·방향 모델

- 패키지: [RapidOcrNet 3.0.0](https://www.nuget.org/packages/RapidOcrNet/3.0.0)
- 패키지 파일: [rapidocrnet.3.0.0.nupkg](https://api.nuget.org/v3-flatcontainer/rapidocrnet/3.0.0/rapidocrnet.3.0.0.nupkg)
- 패키지 SHA-256: `356699916DA0BE3D89B27E4068DAAC5E80947D5972D8A19EDEF6D3AB1432E5A0`
- 저장소 리비전: [`590d44b2efc7ecbef446c36bb6359ebbfcffeb50`](https://github.com/BobLd/RapidOcrNet/tree/590d44b2efc7ecbef446c36bb6359ebbfcffeb50)
- 라이선스: [Apache License 2.0](https://github.com/BobLd/RapidOcrNet/blob/590d44b2efc7ecbef446c36bb6359ebbfcffeb50/LICENSE)

`det.onnx`와 `cls.onnx`는 NuGet 복원 폴더에서 빌드 리소스로 직접 포함하며 별도
변환을 거치지 않는다.

### 한국어 인식 모델과 문자 사전

- ONNX 리비전: [`5c6f574b8e2230adf4287b33e736d71b9fabd28e`](https://huggingface.co/PaddlePaddle/korean_PP-OCRv5_mobile_rec_onnx/tree/5c6f574b8e2230adf4287b33e736d71b9fabd28e)
- ONNX 원본: [`inference.onnx`](https://huggingface.co/PaddlePaddle/korean_PP-OCRv5_mobile_rec_onnx/resolve/5c6f574b8e2230adf4287b33e736d71b9fabd28e/inference.onnx)
- 문자 사전 설정 리비전: [`24b085d9d3d9153a21d97f585fcaaee7a362a487`](https://huggingface.co/PaddlePaddle/korean_PP-OCRv5_mobile_rec/tree/24b085d9d3d9153a21d97f585fcaaee7a362a487)
- 문자 사전 원본: [`config.json`](https://huggingface.co/PaddlePaddle/korean_PP-OCRv5_mobile_rec/resolve/24b085d9d3d9153a21d97f585fcaaee7a362a487/config.json)
- 라이선스: [Apache License 2.0](https://huggingface.co/PaddlePaddle/korean_PP-OCRv5_mobile_rec_onnx/tree/5c6f574b8e2230adf4287b33e736d71b9fabd28e)

`korean-rec.onnx`는 공식 ONNX 파일과 바이트 단위로 일치한다. 한국어 문자 사전은
공식 `config.json`의 `PostProcess.character_dict`를 순서대로 한 줄에 한 문자씩
저장한 파일이며 11,945개 항목을 모두 대조했다.

### 중국어·일본어·영어 통합 인식 모델과 문자 사전

- ONNX 리비전: [`ed152b8b495f84de93cda5709d768548a9127622`](https://huggingface.co/PaddlePaddle/PP-OCRv5_mobile_rec_onnx/tree/ed152b8b495f84de93cda5709d768548a9127622)
- ONNX 원본: [`inference.onnx`](https://huggingface.co/PaddlePaddle/PP-OCRv5_mobile_rec_onnx/resolve/ed152b8b495f84de93cda5709d768548a9127622/inference.onnx)
- 문자 사전 리비전: [`a38c087bcb2579f9ccc2068aea02ec893b1c2311`](https://github.com/PaddlePaddle/PaddleOCR/blob/a38c087bcb2579f9ccc2068aea02ec893b1c2311/ppocr/utils/dict/ppocrv5_dict.txt)
- 라이선스: [Apache License 2.0](https://github.com/PaddlePaddle/PaddleOCR/blob/main/LICENSE)

`cjk-rec.onnx`와 `cjk-dict.txt`는 위 공식 파일과 각각 바이트 단위로 일치한다.

## 확인 방법

저장소에 포함된 파일은 PowerShell에서 다음과 같이 다시 확인할 수 있다.

```powershell
Get-FileHash src/Sentory.Platform.Windows/Ocr/Models/* -Algorithm SHA256
```

감지·방향 모델은 NuGet 복원 뒤 아래 경로에서 확인할 수 있다.

```powershell
Get-FileHash "$env:USERPROFILE/.nuget/packages/rapidocrnet/3.0.0/models/v5/*.onnx" -Algorithm SHA256
```
