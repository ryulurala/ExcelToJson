const XLSX = require("xlsx");

// ============================================================================
// 1. [!규칙] 시트 (가이드 전용 무시 시트)
// ============================================================================
const wsRulesData = [
  ["📌 Excel to JSON 변환 규칙 안내서", "", "", ""],
  [],
  ["💡 1. 주석 처리 (! 기호)", "설명", "작성 예시", "결과"],
  ["시트 주석", "시트 이름이 '!'로 시작하면 해당 시트는 무시됩니다.", "시트명: '!규칙'", "JSON 추출 안 됨"],
  ["컬럼 주석", "컬럼 헤더 이름이 '!'로 시작하면 해당 열은 무시됩니다.", "헤더명: '!메모'", "JSON 추출 안 됨"],
  [],
  ["💡 2. 배열 변환 ([] 기호)", "설명", "작성 예시", "결과"],
  ["자동 배열화", "헤더 끝에 '[]'가 붙으면 데이터를 쉼표(,) 기준으로 잘라 배열로 만듭니다.", "101,102", '["101", "102"]'],
  [],
  ["💡 3. 테이블 타입 기호", "사용처", "JSON 구조 요약", ""],
  ["{} (단일 객체)", "단일 환경 설정, 전역 변수", "{ 'GameConfig': { 'version': '1.0' } }", ""],
  [
    "{{}} (중첩 객체)",
    "고유 ID가 있는 데이터 (아이템 도감, 몬스터 도감 등)",
    "{ 'ItemData': { 'ITEM_01': { 'price': 100 } } }",
    "",
  ],
  [
    "[{}] (객체 배열)",
    "순서가 중요한 데이터 (레벨별 필요 경험치, 출석 보상 등)",
    "{ 'LevelData': [ { 'level': 1 }, { 'level': 2 } ] }",
    "",
  ],
  ["{[]} (배열 객체)", "특정 좌표 모음, 패턴 목록 등 (열 단위 묶음)", "{ 'SpawnPoints': { 'x': [1, 2], 'y': [3, 4] } }", ""],
];

// ============================================================================
// 2. 실제 데이터 시트들 (대중적인 판타지 RPG 데이터)
// ============================================================================

// 설정 데이터 ({} 단일 객체)
const wsConfigData = [
  ["#GameConfig{}", "$key", "$value", "!메모"],
  ["", "version", "1.0.0", "출시 버전"],
  ["", "max_level", 99, "최대 달성 레벨"],
  ["", "start_items[]", "potion_small,wooden_sword", "신규 유저 지급 아이템"],
];

// 아이템 데이터 ({{}} 중첩 객체)
const wsItemData = [
  ["#ItemData{{}}", "$key", "name", "type", "price", "!기획코멘트"],
  ["", "item_001", "빨간 포션", "consumable", 50, "국룰 회복약"],
  ["", "item_002", "철검", "weapon", 500, "초보자용 무기"],
  ["", "item_003", "마법사의 지팡이", "weapon", 1200, "마력 +10"],
];

// 레벨 데이터 ([{}] 객체 배열)
const wsLevelData = [
  ["#LevelData[{}]", "level", "req_exp", "unlock_features[]", "!비고"],
  ["", 1, 0, "", "시작 레벨"],
  ["", 2, 100, "shop", "상점 해금"],
  ["", 3, 300, "guild,pvp", "길드 및 PVP 해금"],
];

// 몬스터 스폰 데이터 ({[]} 배열 객체)
const wsSpawnData = [
  ["#MonsterSpawn{[]}", "monster_id[]", "pos_x[]", "pos_y[]", "!스폰위치"],
  ["", "mob_slime", 10.5, 20.0, "마을 앞마당"],
  ["", "mob_goblin", -5.0, 12.5, "초보자 숲 입구"],
  ["", "mob_dragon", 100.0, 100.0, "화산 지대 보스룸"],
];

// ============================================================================
// 3. 엑셀 워크북 조립 및 서식 지정
// ============================================================================
const wb = XLSX.utils.book_new();

const wsRules = XLSX.utils.aoa_to_sheet(wsRulesData);
const wsConfig = XLSX.utils.aoa_to_sheet(wsConfigData);
const wsItem = XLSX.utils.aoa_to_sheet(wsItemData);
const wsLevel = XLSX.utils.aoa_to_sheet(wsLevelData);
const wsSpawn = XLSX.utils.aoa_to_sheet(wsSpawnData);

// 열 너비 지정 (가독성 향상)
wsRules["!cols"] = [{ wch: 20 }, { wch: 70 }, { wch: 45 }, { wch: 30 }];
wsConfig["!cols"] = [{ wch: 20 }, { wch: 20 }, { wch: 25 }, { wch: 30 }];
wsItem["!cols"] = [{ wch: 20 }, { wch: 15 }, { wch: 20 }, { wch: 15 }, { wch: 10 }, { wch: 25 }];
wsLevel["!cols"] = [{ wch: 20 }, { wch: 10 }, { wch: 15 }, { wch: 25 }, { wch: 25 }];
wsSpawn["!cols"] = [{ wch: 20 }, { wch: 20 }, { wch: 15 }, { wch: 15 }, { wch: 25 }];

// 시트 추가
XLSX.utils.book_append_sheet(wb, wsRules, "!규칙");
XLSX.utils.book_append_sheet(wb, wsConfig, "GameConfig");
XLSX.utils.book_append_sheet(wb, wsItem, "ItemData");
XLSX.utils.book_append_sheet(wb, wsLevel, "LevelData");
XLSX.utils.book_append_sheet(wb, wsSpawn, "MonsterSpawn");

// 파일 저장
const fileName = "sample.xlsx";
XLSX.writeFile(wb, fileName);

console.log(`✅ 범용 RPG 게임 샘플 엑셀 파일이 생성되었습니다: ${fileName}`);
