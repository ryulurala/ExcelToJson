const { getPrettyValue, parseArrayValue } = require("./utils");

function compileSimpleTable(sheetData, startRow, keyIndex) {
  const value = {};
  let row = startRow;
  const keyCol = keyIndex["$key"];
  let valCol = keyIndex["$value"] ?? keyIndex["$value[]"];
  let isArrayValue = keyIndex["$value[]"] !== undefined;

  if (keyCol === undefined || valCol === undefined) return null;

  while (sheetData[row] && sheetData[row][keyCol]) {
    const key = sheetData[row][keyCol];
    const cellValue = sheetData[row][valCol];
    value[key] = isArrayValue ? parseArrayValue(cellValue) : getPrettyValue(cellValue);
    row++;
  }
  return value;
}

function compileObjectObjectTable(sheetData, startRow, keyIndex) {
  const value = {};
  let row = startRow;
  const keyCol = keyIndex["$key"];
  if (keyCol === undefined) return null;

  while (sheetData[row] && sheetData[row][keyCol]) {
    const key = sheetData[row][keyCol];
    const obj = {};
    for (let subkey in keyIndex) {
      if (subkey === "$key") continue;
      const colIdx = keyIndex[subkey];
      const cellValue = sheetData[row][colIdx];
      if (subkey.endsWith("[]")) {
        obj[subkey.slice(0, -2)] = parseArrayValue(cellValue);
      } else {
        obj[subkey] = getPrettyValue(cellValue);
      }
    }
    value[key] = obj;
    row++;
  }
  return value;
}

function compileArrayObjectTable(sheetData, startRow, keyIndex) {
  const value = [];
  let row = startRow;

  while (sheetData[row]) {
    const obj = {};
    let isSane = false;
    for (let subkey in keyIndex) {
      const colIdx = keyIndex[subkey];
      const cellValue = sheetData[row][colIdx];
      if (subkey.endsWith("[]")) {
        const cleanKey = subkey.slice(0, -2);
        obj[cleanKey] = parseArrayValue(cellValue);
        if (obj[cleanKey].length > 0) isSane = true;
      } else {
        obj[subkey] = getPrettyValue(cellValue);
        if (obj[subkey] !== "") isSane = true;
      }
    }
    if (!isSane) break;
    value.push(obj);
    row++;
  }
  return value;
}

function compileObjectArrayTable(sheetData, startRow, keyIndex) {
  const value = {};
  for (let subkey in keyIndex) {
    let isArray = subkey.endsWith("[]");
    let cleanKey = isArray ? subkey.slice(0, -2) : subkey;
    const colIdx = keyIndex[subkey];
    const objArray = [];
    let r = startRow;
    while (sheetData[r] && sheetData[r][colIdx] !== undefined && sheetData[r][colIdx] !== "") {
      const cellValue = sheetData[r][colIdx];
      objArray.push(isArray ? parseArrayValue(cellValue) : getPrettyValue(cellValue));
      r++;
    }
    value[cleanKey] = objArray;
  }
  return value;
}

module.exports = {
  compileSimpleTable,
  compileObjectObjectTable,
  compileArrayObjectTable,
  compileObjectArrayTable,
};
