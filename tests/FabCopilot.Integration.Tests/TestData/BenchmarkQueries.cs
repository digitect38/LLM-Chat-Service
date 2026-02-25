namespace FabCopilot.Integration.Tests.TestData;

public static class BenchmarkQueries
{
    public static readonly BenchmarkQuery Q1 = new(
        Id: "Q1",
        Text: "CMP 패드 교체 시기는?",
        Category: "Procedure",
        ExpectedKeywords: ["패드", "교체", "시간", "수명", "wafer"]);

    public static readonly BenchmarkQuery Q2 = new(
        Id: "Q2",
        Text: "슬러리 공급 압력 이상 시 알람코드와 대처 방법은?",
        Category: "Troubleshooting",
        ExpectedKeywords: ["슬러리", "압력", "알람", "대처"]);

    public static readonly BenchmarkQuery Q3 = new(
        Id: "Q3",
        Text: "웨이퍼 스크래치 발생 원인과 해결방법",
        Category: "Troubleshooting",
        ExpectedKeywords: ["스크래치", "원인", "해결", "웨이퍼"]);

    public static readonly BenchmarkQuery Q4 = new(
        Id: "Q4",
        Text: "CMP 장비의 일일 점검 항목은?",
        Category: "Procedure",
        ExpectedKeywords: ["점검", "일일", "항목", "CMP"]);

    public static readonly BenchmarkQuery Q5 = new(
        Id: "Q5",
        Text: "MRR이 낮아지는 원인은?",
        Category: "Troubleshooting",
        ExpectedKeywords: ["MRR", "원인", "removal", "rate"]);

    public static IEnumerable<BenchmarkQuery> All =>
        [Q1, Q2, Q3, Q4, Q5];
}

public sealed record BenchmarkQuery(
    string Id,
    string Text,
    string Category,
    string[] ExpectedKeywords);
