const path = require("path");

const CURRENT_DIR = process.pkg ? path.dirname(process.execPath) : process.cwd();

const argSource = process.argv[2];
const argTarget = process.argv[3];

const SOURCE_DIR = argSource ? path.resolve(argSource) : CURRENT_DIR;
const TARGET_DIR = argTarget ? path.resolve(argTarget) : null;
const PRETTY_OUTPUT = true;

function getPrettyValue(value) {
  if (value === null || value === undefined || value === "") return "";
  if (typeof value === "number") return value;
  const strVal = String(value).trim();
  const strWithoutComma = strVal.replace(/,/g, "");
  if (strVal !== "" && !isNaN(Number(strWithoutComma))) {
    return Number(strWithoutComma);
  }
  return strVal;
}

function parseArrayValue(csvString) {
  if (!csvString) return [];
  return String(csvString)
    .split(",")
    .map((item) => getPrettyValue(item));
}

module.exports = {
  SOURCE_DIR,
  TARGET_DIR,
  PRETTY_OUTPUT,
  getPrettyValue,
  parseArrayValue,
};
