import { compileTable } from "./compiler.js";
import { downloadBlob } from "./utils.js";

const dropzone = document.getElementById("dropzone");
const logArea = document.getElementById("log-area");

function log(message, type = "normal") {
  const div = document.createElement("div");
  div.innerHTML = message;
  if (type === "success") div.className = "log-success";
  if (type === "error") div.className = "log-error";
  if (type === "warn") div.className = "log-warn";
  logArea.appendChild(div);
  logArea.scrollTop = logArea.scrollHeight;
}

dropzone.addEventListener("dragover", (e) => {
  e.preventDefault();
  dropzone.classList.add("hover");
});
dropzone.addEventListener("dragleave", () => dropzone.classList.remove("hover"));

dropzone.addEventListener("drop", async (e) => {
  e.preventDefault();
  dropzone.classList.remove("hover");

  log("<hr>▶ 새로운 변환 작업을 시작합니다...");

  const files = await getAllFilesFromDataTransfer(e.dataTransfer.items);
  const validFiles = files.filter((f) => f.name.endsWith(".xlsx") || f.name.endsWith(".xls"));

  if (validFiles.length === 0) {
    log("⚠️ 처리할 엑셀 파일을 찾을 수 없습니다.", "warn");
    return;
  }

  log(`총 ${validFiles.length}개의 엑셀 파일이 발견되었습니다. 변환 중...`);

  const jsonResults = [];
  for (const file of validFiles) {
    const parsedResults = await processExcelFile(file); // 🌟 이제 배열이 반환됩니다.
    if (parsedResults && Array.isArray(parsedResults)) {
      jsonResults.push(...parsedResults); // 배열 요소를 풀어서 추가
    }
  }

  if (jsonResults.length === 0) {
    log("⚠️ 추출 규칙에 맞는 데이터가 없어 저장하지 않았습니다.", "warn");
  } else if (jsonResults.length === 1) {
    log(`📦 1개의 시트 변환 완료. JSON으로 직접 저장합니다...`, "success");
    downloadBlob(jsonResults[0].fileName, jsonResults[0].content, "application/json");
  } else {
    log(`📦 총 ${jsonResults.length}개의 시트 변환 완료. ZIP으로 압축합니다...`, "normal");
    const zip = new JSZip();
    jsonResults.forEach((item) => {
      zip.file(item.fileName, item.content);
    });
    const zipBlob = await zip.generateAsync({ type: "blob" });
    downloadBlob("Exported_JSON_Data.zip", zipBlob, "application/zip");
    log(`✅ 압축 파일(Exported_JSON_Data.zip) 다운로드 완료!`, "success");
  }
});

// 파일 스캐너 (이전과 동일)
async function getAllFilesFromDataTransfer(items) {
  const files = [];
  const entries = [];
  for (let i = 0; i < items.length; i++) {
    if (items[i].kind === "file") entries.push(items[i].webkitGetAsEntry());
  }

  async function readEntry(entry) {
    if (entry.isFile) return new Promise((resolve) => entry.file(resolve));
    else if (entry.isDirectory) {
      const dirReader = entry.createReader();
      const entries = await new Promise((resolve) => dirReader.readEntries(resolve));
      const promises = entries.map(readEntry);
      const results = await Promise.all(promises);
      return results.flat();
    }
    return [];
  }

  for (const entry of entries) {
    if (entry) {
      const result = await readEntry(entry);
      files.push(...(Array.isArray(result) ? result : [result]));
    }
  }
  return files;
}

// 🌟 시트별로 JSON을 나누도록 수정된 파싱 함수
async function processExcelFile(file) {
  return new Promise((resolve) => {
    log(` ⏳ 분석 중: ${file.name}`);
    const reader = new FileReader();
    reader.onload = function (e) {
      try {
        const data = new Uint8Array(e.target.result);
        const workbook = XLSX.read(data, { type: "array" });

        const sheetResults = []; // 🌟 이 파일에서 추출된 모든 시트의 결과를 담을 배열

        workbook.SheetNames.forEach((sheetName) => {
          if (sheetName.startsWith("!")) return;

          const sheet = workbook.Sheets[sheetName];
          const sheetData = XLSX.utils.sheet_to_json(sheet, { header: 1, defval: "" });
          let sheetObject = {}; // 🌟 시트 단위로 객체 생성

          for (let row = 0; row < sheetData.length; row++) {
            const line = sheetData[row];
            if (!line || line.length === 0) continue;
            const anchor = String(line[0] || "").trim();
            if (!anchor.startsWith("#")) continue;

            const match = anchor.match(/^#(\w+)(.*)$/);
            if (!match) continue;

            const objectName = match[1];
            const objectType = match[2];
            const keyIndex = {};

            for (let col = 1; col < line.length; col++) {
              const key = String(line[col]).trim();
              if (key.length > 0 && !key.startsWith("!")) keyIndex[key] = col;
            }

            const parsedData = compileTable(sheetData, row + 1, keyIndex, objectType);
            if (parsedData) sheetObject[objectName] = parsedData;
          }

          // 해당 시트에 파싱된 데이터가 있으면 결과 배열에 추가
          if (Object.keys(sheetObject).length > 0) {
            const fileName = `${sheetName}.json`; // 🌟 시트 이름을 파일 이름으로 지정
            const content = JSON.stringify(sheetObject, null, 2);
            log(`  -> 추출 완료: [${sheetName}] 시트`);
            sheetResults.push({ fileName, content });
          }
        });

        // 결과 반환
        if (sheetResults.length > 0) {
          resolve(sheetResults); // 🌟 여러 시트 결과를 배열 형태로 반환
        } else {
          log(`  -> ⚠️ 스킵됨 (유효한 테이블 없음): ${file.name}`, "warn");
          resolve(null);
        }
      } catch (error) {
        log(`  -> ❌ 파싱 오류: ${error.message}`, "error");
        resolve(null);
      }
    };
    reader.readAsArrayBuffer(file);
  });
}
