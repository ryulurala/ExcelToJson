const { getPrettyValue, parseArrayValue } = require("./utils");

function isNewTableAnchor(line) {
  if (!line || line.length === 0) return false;
  const anchor = String(line[0] || "").trim();
  return anchor.startsWith("#");
}

function compileSimpleTable(sheetData, startRow, keyIndex) {
  const value = {};
  const keyCol = keyIndex["$key"];
  let valCol = keyIndex["$value"] ?? keyIndex["$value[]"];
  let isArrayValue = keyIndex["$value[]"] !== undefined;

  if (keyCol === undefined || valCol === undefined) return null;

  for (let row = startRow; row < sheetData.length; row++) {
    const line = sheetData[row];
    if (!line || line.length === 0) continue; // 빈 행 건너뛰기
    if (isNewTableAnchor(line)) break; // 다음 테이블 앵커를 만나면 종료

    const key = line[keyCol];
    if (key !== undefined && key !== "") {
      const cellValue = line[valCol];
      value[key] = isArrayValue ? parseArrayValue(cellValue) : getPrettyValue(cellValue);
    }
  }
  return value;
}

function compileObjectObjectTable(sheetData, startRow, keyIndex) {
  const value = {};
  const keyCol = keyIndex["$key"];
  if (keyCol === undefined) return null;

  for (let row = startRow; row < sheetData.length; row++) {
    const line = sheetData[row];
    if (!line || line.length === 0) continue;
    if (isNewTableAnchor(line)) break;

    const key = line[keyCol];
    if (key !== undefined && key !== "") {
      const obj = {};
      for (let subkey in keyIndex) {
        if (subkey === "$key") continue;
        const colIdx = keyIndex[subkey];
        const cellValue = line[colIdx];

        if (subkey.endsWith("[]")) {
          obj[subkey.slice(0, -2)] = parseArrayValue(cellValue);
        } else {
          obj[subkey] = getPrettyValue(cellValue);
        }
      }
      value[key] = obj;
    }
  }
  return value;
}

function compileArrayObjectTable(sheetData, startRow, keyIndex) {
  const value = [];

  for (let row = startRow; row < sheetData.length; row++) {
    const line = sheetData[row];
    if (!line || line.length === 0) continue;
    if (isNewTableAnchor(line)) break;

    const obj = {};
    let isSane = false;

    for (let subkey in keyIndex) {
      const colIdx = keyIndex[subkey];
      const cellValue = line[colIdx];

      if (subkey.endsWith("[]")) {
        const cleanKey = subkey.slice(0, -2);
        obj[cleanKey] = parseArrayValue(cellValue);
        if (obj[cleanKey].length > 0) isSane = true;
      } else {
        obj[subkey] = getPrettyValue(cellValue);
        if (obj[subkey] !== "") isSane = true;
      }
    }

    if (isSane) {
      value.push(obj);
    }
  }
  return value;
}

function compileObjectArrayTable(sheetData, startRow, keyIndex) {
  const value = {};

  // 1. 결과 배열 사전 초기화
  for (let subkey in keyIndex) {
    let isArray = subkey.endsWith("[]");
    let cleanKey = isArray ? subkey.slice(0, -2) : subkey;
    value[cleanKey] = [];
  }

  // 2. 전체 행을 한 번만 순회하며 각 컬럼에 데이터 푸시
  for (let row = startRow; row < sheetData.length; row++) {
    const line = sheetData[row];
    if (!line || line.length === 0) continue;
    if (isNewTableAnchor(line)) break;

    for (let subkey in keyIndex) {
      let isArray = subkey.endsWith("[]");
      let cleanKey = isArray ? subkey.slice(0, -2) : subkey;
      const colIdx = keyIndex[subkey];
      const cellValue = line[colIdx];

      if (cellValue !== undefined && cellValue !== "") {
        value[cleanKey].push(isArray ? parseArrayValue(cellValue) : getPrettyValue(cellValue));
      }
    }
  }
  return value;
}

module.exports = {
  compileSimpleTable,
  compileObjectObjectTable,
  compileArrayObjectTable,
  compileObjectArrayTable,
};
