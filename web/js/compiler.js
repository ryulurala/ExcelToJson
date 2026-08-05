import { getPrettyValue, parseArrayValue } from "./utils.js";

// 🌟 다음 테이블 앵커('#')가 시작되는지 확인하는 헬퍼 함수
function isNewTableAnchor(line) {
  if (!line || line.length === 0) return false;
  const anchor = String(line[0] || "").trim();
  return anchor.startsWith("#");
}

export function compileTable(sheetData, startRow, keyIndex, type) {
  const value = type === "[{}]" ? [] : {};
  const keyCol = keyIndex["$key"];

  // 1. {[]} (배열 객체) 타입 처리 로직
  if (type === "{[]}") {
    // 결과 배열 사전 초기화
    for (let subkey in keyIndex) {
      let isArray = subkey.endsWith("[]");
      let cleanKey = isArray ? subkey.slice(0, -2) : subkey;
      value[cleanKey] = [];
    }

    // 전체 행을 순회하며 빈 행은 무시, 앵커 만나면 파싱 종료
    for (let r = startRow; r < sheetData.length; r++) {
      const line = sheetData[r];
      if (!line || line.length === 0) continue;
      if (isNewTableAnchor(line)) break;

      for (let subkey in keyIndex) {
        let isArray = subkey.endsWith("[]");
        let cleanKey = isArray ? subkey.slice(0, -2) : subkey;
        const colIdx = keyIndex[subkey];
        const cellValue = line[colIdx];

        // 값이 존재할 때만 배열에 추가
        if (cellValue !== undefined && cellValue !== "") {
          value[cleanKey].push(isArray ? parseArrayValue(cellValue) : getPrettyValue(cellValue));
        }
      }
    }
    return value;
  }

  // 2. {}, {{}}, [{}] 타입 공통 처리 로직
  for (let row = startRow; row < sheetData.length; row++) {
    const line = sheetData[row];
    if (!line || line.length === 0) continue; // 빈 행 건너뛰기
    if (isNewTableAnchor(line)) break; // 다음 테이블 앵커를 만나면 파싱 종료

    const key = type === "[{}]" ? null : line[keyCol];

    // [{}] (객체 배열) 타입이 아닌데 키 값이 비어있다면, 유효한 데이터가 아니라고 판단해 건너뜀
    if (type !== "[{}]" && (key === undefined || key === "")) continue;

    let obj = null;

    if (type === "{}") {
      // {} (단일 객체) 처리 로직
      let valCol = keyIndex["$value"] ?? keyIndex["$value[]"];
      if (valCol !== undefined) {
        obj = keyIndex["$value[]"] !== undefined ? parseArrayValue(line[valCol]) : getPrettyValue(line[valCol]);
      }
    } else {
      // {{}}, [{}] (중첩 객체, 객체 배열) 처리 로직
      obj = {};
      let isSane = false;
      for (let subkey in keyIndex) {
        if (subkey === "$key" && type !== "[{}]") continue;
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

      // [{}] 타입일 때 유효한 데이터(isSane)가 없으면 배열에 추가하지 않고 건너뜀
      if (type === "[{}]" && !isSane) continue;
    }

    // 결과 객체 및 배열에 병합
    if (type === "[{}]") {
      if (obj) value.push(obj);
    } else if (type === "{{}}" || type === "{}") {
      if (obj !== null) value[key] = obj;
    }
  }

  return value;
}
