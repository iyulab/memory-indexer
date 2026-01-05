# Twenty Questions Game - AI vs AI Demo

이 샘플은 **memory-indexer**의 핵심 기능을 증명합니다: 대화 히스토리 없이 메모리만으로 컨텍스트 유지

## 핵심 개념

```
┌─────────────────────────────────────────────────────────────────┐
│                    전통적인 방식 (채팅 히스토리 전달)              │
├─────────────────────────────────────────────────────────────────┤
│  LLM API 호출:                                                   │
│  messages: [                                                     │
│    { system: "..." },                                            │
│    { user: "Is it alive?" },                                     │
│    { assistant: "Yes" },                                         │
│    { user: "Is it an animal?" },                                 │
│    { assistant: "Yes" },                                         │
│    { user: "Is it a pet?" },      ← 모든 히스토리 전달           │
│    ...                                                           │
│  ]                                                               │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                    이 데모 (Memory-Only)                         │
├─────────────────────────────────────────────────────────────────┤
│  LLM API 호출:                                                   │
│  messages: [                                                     │
│    { system: "너의 기억:\n[recall된 메모리들...]" },              │
│    { user: "Yes" }               ← 상대방 응답 1개만!            │
│  ]                                                               │
│                                                                  │
│  컨텍스트 = 100% memory-indexer recall                          │
└─────────────────────────────────────────────────────────────────┘
```

## 게임 플로우

```
Round 1:
  Alpha: "게임 시작! 질문하세요"
    ↓ (Beta는 이 메시지 1개만 받음)
  Beta: [메모리 recall] → "Is it alive?"
    ↓ (Alpha는 이 질문 1개만 받음)
  Alpha: [메모리 recall - secret 확인] → "Yes"

Round 2:
  Beta: [메모리 recall - 이전 Q&A 기억] → "Is it an animal?"
    ↓
  Alpha: [메모리 recall] → "Yes"

... (20 라운드 계속)
```

## 메모리 격리

Alpha와 Beta는 완전히 분리된 메모리 공간을 사용합니다:

| Agent | User ID | 저장하는 기억 |
|-------|---------|---------------|
| Alpha | `alpha_quizmaster` | 비밀 답, 게임 규칙, Q&A 기록 |
| Beta | `beta_guesser` | 게임 규칙, 전략, Q&A 기록, 추론 |

## 주요 기능 시연

### 1. 중복 질문 감지
Alpha는 벡터 유사도로 중복 질문을 탐지합니다:
```csharp
var duplicateCheck = await memoryService.RecallAsync(ALPHA_USER_ID, betaQuestion, limit: 5);
var isDuplicate = duplicateCheck.Any(m => m.Score > 0.85f);
```

### 2. 추론 저장
Beta는 각 Q&A에서 추론을 저장합니다:
```
[DEDUCTION_R3] CONFIRMED: The secret HAS the property "alive"
[DEDUCTION_R4] RULED OUT: The secret does NOT have the property "bigger than a car"
```

### 3. 라운드 추적
각 AI는 현재 라운드를 메모리에 저장하고 recall합니다.

### 4. 20번째 라운드 강제 추측
Beta는 마지막 라운드에서 반드시 정답을 추측합니다.

### 5. LLM 재시도 메커니즘
LLM 호출 실패 시 exponential backoff로 최대 3회 재시도:
```csharp
// 재시도 간격: 1초 → 2초 → 4초 (exponential backoff)
// 재시도마다 temperature를 0.1씩 증가 (0.7 → 0.8 → 0.9)
var delay = baseDelay * Math.Pow(2, attempt - 1);
```

## 실행 방법

```bash
# 저장소 루트의 .env 파일 설정 필요
cd samples/TwentyQuestionsGame
dotnet run
```

### 필요한 환경 변수 (.env)
```
OPENAI_API_KEY=sk-your-api-key

# Optional: 채팅 모델 선택 (기본값: gpt-4o-mini)
OPENAI_CHAT_MODEL=gpt-4o-mini
```

## 샘플 출력

```
╔══════════════════════════════════════════════════════════════╗
║          Twenty Questions Game - Memory Demo                  ║
║          AI vs AI: 상대 응답 1개만 + Memory Recall            ║
╚══════════════════════════════════════════════════════════════╝

[SECRET] Alpha is thinking of: "a golden retriever" (hidden from Beta)

══════════════════════════ Round 1/20 ══════════════════════════

[BETA] Received from Alpha: "The game has started. Ask your first question!"
[BETA] Recalling memories to understand context...
[BETA] Recalled 4 memories
[BETA] >>> Is it something that is alive?
[ALPHA] Received question: "Is it something that is alive?"
[ALPHA] Recalling memories...
[ALPHA] Recalled 3 memories
[ALPHA] >>> Yes

══════════════════════════ Round 2/20 ══════════════════════════

[BETA] Received from Alpha: "Yes"
[BETA] Recalling memories to understand context...
[BETA] Recalled 7 memories
[BETA] >>> Is it an animal?
...

╔══════════════════════════════════════════════════════════════╗
║  GAME OVER                                                    ║
╚══════════════════════════════════════════════════════════════╝

  🎉 BETA WINS! Successfully guessed: a golden retriever

╔══════════════════════════════════════════════════════════════╗
║  MEMORY STATISTICS                                            ║
╚══════════════════════════════════════════════════════════════╝
  Alpha memories: 28
  Beta memories:  35
  Total:          63

  ┌────────────────────────────────────────────────────────────┐
  │ KEY DEMONSTRATION:                                         │
  │ - Each LLM call received ONLY the opponent's last msg      │
  │ - NO chat history was passed                               │
  │ - Context came 100% from memory-indexer recall             │
  └────────────────────────────────────────────────────────────┘
```

## 왜 이것이 중요한가?

1. **토큰 효율성**: 대화가 길어져도 컨텍스트 윈도우 증가 없음
2. **선택적 회상**: 관련 있는 기억만 recall됨
3. **지속적 컨텍스트**: 세션이 종료되어도 기억 유지
4. **확장성**: 수천 개의 대화에도 동일하게 동작
