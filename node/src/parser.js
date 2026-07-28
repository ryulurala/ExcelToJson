const fs = require("fs");
const path = require("path");
const XLSX = require("xlsx");
const { TARGET_DIR, PRETTY_OUTPUT } = require("./utils");
const {
  compileSimpleTable,
  compileObjectObjectTable,
  compileArrayObjectTable,
  compileObjectArrayTable,
} = require("./compiler");

function processExcelFile(filePath) {
  console.log(`\n⏳ 파일 분석 중: ${path.basename(filePath)}`);
  const workbook = XLSX.readFile(filePath);

  // 🌟 TARGET_DIR이 지정되지 않았다면 현재 엑셀 파일이 있는 폴더를 저장 경로로 사용
  const actualTargetDir = TARGET_DIR || path.dirname(filePath);

  if (!fs.existsSync(actualTargetDir)) fs.mkdirSync(actualTargetDir, { recursive: true });

  workbook.SheetNames.forEach((sheetName) => {
    if (sheetName.startsWith("!")) return;

    const sheet = workbook.Sheets[sheetName];
    const sheetData = XLSX.utils.sheet_to_json(sheet, { header: 1, defval: "" });
    let sheetObject = {};

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

      let compiler = null;
      switch (objectType) {
        case "{}":
          compiler = compileSimpleTable;
          break;
        case "{{}}":
          compiler = compileObjectObjectTable;
          break;
        case "{[]}":
          compiler = compileObjectArrayTable;
          break;
        case "[{}]":
          compiler = compileArrayObjectTable;
          break;
      }

      if (compiler) {
        const parsedData = compiler(sheetData, row + 1, keyIndex);
        if (parsedData) sheetObject[objectName] = parsedData;
      }
    }

    if (Object.keys(sheetObject).length > 0) {
      const fileName = `${sheetName}.json`;
      // 🌟 actualTargetDir(엑셀 파일이 있는 경로 또는 지정된 경로)에 저장
      const outputPath = path.join(actualTargetDir, fileName);
      fs.writeFileSync(outputPath, JSON.stringify(sheetObject, null, PRETTY_OUTPUT ? 2 : 0), "utf-8");
      console.log(`  ✅ 시트 추출 완료: [${sheetName}] -> ${fileName}`);
    }
  });
}

module.exports = { processExcelFile };
