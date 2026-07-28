const fs = require("fs");
const path = require("path");
const { SOURCE_DIR } = require("./utils");
const { processExcelFile } = require("./parser");

try {
  console.log("🚀 Excel to JSON Converter 시작...");
  console.log(`입력 경로: ${SOURCE_DIR}`);

  if (!fs.existsSync(SOURCE_DIR)) {
    console.error(`❌ 지정한 경로를 찾을 수 없습니다: ${SOURCE_DIR}`);
    process.exit(1);
  }

  const stats = fs.statSync(SOURCE_DIR);
  let targetFiles = [];

  // 🌟 1. 단일 파일을 지정했을 경우
  if (stats.isFile()) {
    if (SOURCE_DIR.endsWith(".xlsx") || SOURCE_DIR.endsWith(".xls")) {
      targetFiles.push(SOURCE_DIR);
    } else {
      console.error("❌ 지정한 파일이 엑셀 파일(.xlsx, .xls)이 아닙니다.");
      process.exit(1);
    }
  }
  // 🌟 2. 폴더를 지정했을 경우 (해당 폴더 내 모든 엑셀 검색)
  else if (stats.isDirectory()) {
    const files = fs.readdirSync(SOURCE_DIR);
    targetFiles = files
      .filter((file) => (file.endsWith(".xlsx") || file.endsWith(".xls")) && !file.startsWith("~"))
      .map((file) => path.join(SOURCE_DIR, file));
  }

  if (targetFiles.length === 0) {
    console.log(`📂 해당 경로에 처리할 엑셀 파일이 없습니다.`);
  } else {
    targetFiles.forEach((file) => processExcelFile(file));
    console.log("\n🎉 모든 작업이 완료되었습니다.");
  }
} catch (error) {
  console.error("❌ 실행 중 오류 발생:\n", error);
}
