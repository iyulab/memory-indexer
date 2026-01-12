import './style.css'

// Types
interface User {
  id: string
  name: string
  createdAt: string
  lastActive: string
}

interface Session {
  id: string
  userId: string
  title: string
  createdAt: string
  lastMessage: string
  messageCount: number
}

interface ChatResponse {
  response: string
  memoriesUsed: number
  memories: Array<{ content: string; type: string; score: number }>
}

// Streaming event types
interface StreamEvent {
  type: 'thinking' | 'reasoning' | 'content' | 'error' | 'done'
  data: ThinkingData | ReasoningData | ContentData | ErrorData | DoneData
}

interface ReasoningData {
  delta: string
}

interface ThinkingData {
  step: 'start' | 'buffer' | 'short_term' | 'long_term' | 'done'
  count?: number
}

interface ContentData {
  delta: string
}

interface ErrorData {
  message: string
}

interface DoneData {
  memoriesUsed: number
  totalLength: number
  metrics?: {
    userTokens: number
    aiTokens: number
    contextTokens: number
    totalTokens: number
    recallMs: number
    llmMs: number
    ttftMs: number
    totalMs: number
  }
}

// KPI tracking
interface KpiStats {
  totalTurns: number
  totalUserTokens: number
  totalAiTokens: number
  totalContextTokens: number
  totalRecallMs: number
  totalLlmMs: number
  totalTtftMs: number
}

const kpiStats: KpiStats = {
  totalTurns: 0,
  totalUserTokens: 0,
  totalAiTokens: 0,
  totalContextTokens: 0,
  totalRecallMs: 0,
  totalLlmMs: 0,
  totalTtftMs: 0
}

interface StatusResponse {
  user: string
  total: number
  tiers: {
    buffer: {
      count: number
      totalTokens: number
      items: Array<{ content: string; ageSeconds: number }>
    }
    shortTerm: {
      count: number
      capacity: number
      items: Array<{ content: string; type: string; ageMinutes: number }>
    }
    longTerm: {
      count: number
      sessionCount: number
      userCount: number
    }
    archive: {
      count: number
      confirmed: number
    }
  }
  byType: Record<string, number>
  byScope: { session: number; user: number }
  recent: Array<{
    content: string
    type: string
    tier: string
    scope: string
    createdAt: string
    importance: number
  }>
  config: { embedding: string; chatLlm: string }
}

// State
let currentUser: User | null = null
let currentSession: Session | null = null

// Elements
const loginScreen = document.getElementById('login-screen')!
const mainScreen = document.getElementById('main-screen')!
const userListEl = document.getElementById('user-list')!
const newUserNameInput = document.getElementById('new-user-name') as HTMLInputElement
const createUserBtn = document.getElementById('create-user-btn')!
const currentUserEl = document.getElementById('current-user')!
const switchUserBtn = document.getElementById('switch-user-btn')!
const configEl = document.getElementById('config')!
const sessionListEl = document.getElementById('session-list')!
const newSessionBtn = document.getElementById('new-session-btn')!
const noSessionEl = document.getElementById('no-session')!
const chatContainerEl = document.getElementById('chat-container')!
const messagesEl = document.getElementById('messages')!
const chatForm = document.getElementById('chat-form')!
const messageInput = document.getElementById('message-input') as HTMLInputElement
const kpiStatsEl = document.getElementById('kpi-stats')!
const memoryStatsEl = document.getElementById('memory-stats')!
const recentMemoriesEl = document.getElementById('recent-memories')!
const refreshStatusBtn = document.getElementById('refresh-status')!
const clearMemoriesBtn = document.getElementById('clear-memories')!

// API Functions
const api = {
  async getUsers(): Promise<User[]> {
    const res = await fetch('/api/users')
    return res.json()
  },

  async createUser(name: string): Promise<User> {
    const res = await fetch('/api/users', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name })
    })
    return res.json()
  },

  async getSessions(userId: string): Promise<Session[]> {
    const res = await fetch(`/api/users/${userId}/sessions`)
    return res.json()
  },

  async createSession(userId: string): Promise<Session> {
    const res = await fetch(`/api/users/${userId}/sessions`, { method: 'POST' })
    return res.json()
  },

  async deleteSession(sessionId: string): Promise<void> {
    await fetch(`/api/sessions/${sessionId}`, { method: 'DELETE' })
  },

  async sendMessage(sessionId: string, message: string): Promise<ChatResponse> {
    const res = await fetch('/api/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ sessionId, message })
    })
    return res.json()
  },

  async getStatus(userId: string): Promise<StatusResponse> {
    const res = await fetch(`/api/users/${userId}/status`)
    return res.json()
  },

  async clearMemories(userId: string): Promise<void> {
    await fetch(`/api/users/${userId}/memories`, { method: 'DELETE' })
  },

  // Streaming chat with SSE
  streamMessage(
    sessionId: string,
    message: string,
    callbacks: {
      onThinking?: (data: ThinkingData) => void
      onReasoning?: (delta: string) => void
      onContent?: (delta: string) => void
      onError?: (message: string) => void
      onDone?: (data: DoneData) => void
    }
  ): AbortController {
    const controller = new AbortController()
    const url = `/api/chat/stream?sessionId=${encodeURIComponent(sessionId)}&message=${encodeURIComponent(message)}`

    fetch(url, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) {
          callbacks.onError?.(`HTTP ${response.status}`)
          return
        }

        const reader = response.body?.getReader()
        if (!reader) {
          callbacks.onError?.('No response body')
          return
        }

        const decoder = new TextDecoder()
        let buffer = ''

        while (true) {
          const { done, value } = await reader.read()
          if (done) break

          buffer += decoder.decode(value, { stream: true })
          const lines = buffer.split('\n')
          buffer = lines.pop() || ''

          for (const line of lines) {
            if (line.startsWith('data: ')) {
              try {
                const event = JSON.parse(line.slice(6)) as StreamEvent
                console.log('[SSE] Received:', event.type, event.data)
                switch (event.type) {
                  case 'thinking':
                    callbacks.onThinking?.(event.data as ThinkingData)
                    break
                  case 'reasoning':
                    callbacks.onReasoning?.((event.data as ReasoningData).delta)
                    break
                  case 'content':
                    callbacks.onContent?.((event.data as ContentData).delta)
                    break
                  case 'error':
                    callbacks.onError?.((event.data as ErrorData).message)
                    break
                  case 'done':
                    callbacks.onDone?.(event.data as DoneData)
                    break
                }
              } catch {
                // Ignore parse errors
              }
            }
          }
        }
      })
      .catch((err) => {
        if (err.name !== 'AbortError') {
          callbacks.onError?.(err.message)
        }
      })

    return controller
  }
}

// Utility Functions
function escapeHtml(text: string): string {
  const div = document.createElement('div')
  div.textContent = text
  return div.innerHTML
}

function formatTime(dateStr: string): string {
  const date = new Date(dateStr)
  const now = new Date()
  const diff = now.getTime() - date.getTime()
  const mins = Math.floor(diff / 60000)
  const hours = Math.floor(diff / 3600000)
  const days = Math.floor(diff / 86400000)

  if (mins < 1) return 'just now'
  if (mins < 60) return `${mins}m ago`
  if (hours < 24) return `${hours}h ago`
  return `${days}d ago`
}

// Render Functions
async function renderUserList() {
  const users = await api.getUsers()

  if (users.length === 0) {
    userListEl.innerHTML = '<p style="color:#666;text-align:center;padding:1rem">No users yet</p>'
    return
  }

  userListEl.innerHTML = users.map(u => `
    <div class="user-item" data-id="${u.id}">
      <span class="name">${escapeHtml(u.name)}</span>
      <span class="meta">${formatTime(u.lastActive)}</span>
    </div>
  `).join('')

  userListEl.querySelectorAll('.user-item').forEach(el => {
    el.addEventListener('click', () => {
      const userId = (el as HTMLElement).dataset.id!
      const user = users.find(u => u.id === userId)!
      selectUser(user)
    })
  })
}

async function renderSessionList() {
  if (!currentUser) return

  const sessions = await api.getSessions(currentUser.id)

  if (sessions.length === 0) {
    sessionListEl.innerHTML = '<p style="color:#666;text-align:center;padding:1rem;font-size:0.8rem">No sessions</p>'
    return
  }

  sessionListEl.innerHTML = sessions.map(s => `
    <div class="session-item ${currentSession?.id === s.id ? 'active' : ''}" data-id="${s.id}">
      <div class="title">${escapeHtml(s.title)}</div>
      <div class="info">${s.messageCount} msgs · ${formatTime(s.lastMessage)}</div>
    </div>
  `).join('')

  sessionListEl.querySelectorAll('.session-item').forEach(el => {
    el.addEventListener('click', () => {
      const sessionId = (el as HTMLElement).dataset.id!
      const session = sessions.find(s => s.id === sessionId)!
      selectSession(session)
    })
  })
}

function renderKpi() {
  if (kpiStats.totalTurns === 0) {
    kpiStatsEl.innerHTML = ''
    return
  }

  const avgUserTokens = Math.round(kpiStats.totalUserTokens / kpiStats.totalTurns)
  const avgAiTokens = Math.round(kpiStats.totalAiTokens / kpiStats.totalTurns)
  const avgTtft = Math.round(kpiStats.totalTtftMs / kpiStats.totalTurns)
  const totalTokens = kpiStats.totalUserTokens + kpiStats.totalAiTokens + kpiStats.totalContextTokens
  const avgSpeed = kpiStats.totalLlmMs > 0
    ? ((kpiStats.totalAiTokens / kpiStats.totalLlmMs) * 1000).toFixed(1)
    : '0'

  kpiStatsEl.innerHTML = `
    <div class="monitor-section kpi-section">
      <h4 class="section-title">📊 Session KPI</h4>
      <div class="kpi-grid">
        <div class="kpi-item">
          <span class="kpi-value">${kpiStats.totalTurns}</span>
          <span class="kpi-label">Turns</span>
        </div>
        <div class="kpi-item">
          <span class="kpi-value">${totalTokens.toLocaleString()}</span>
          <span class="kpi-label">Total Tokens</span>
        </div>
        <div class="kpi-item">
          <span class="kpi-value">${avgUserTokens}</span>
          <span class="kpi-label">Avg User</span>
        </div>
        <div class="kpi-item">
          <span class="kpi-value">${avgAiTokens}</span>
          <span class="kpi-label">Avg AI</span>
        </div>
        <div class="kpi-item">
          <span class="kpi-value">${avgSpeed}</span>
          <span class="kpi-label">tok/s</span>
        </div>
        <div class="kpi-item">
          <span class="kpi-value">${avgTtft}ms</span>
          <span class="kpi-label">Avg TTFT</span>
        </div>
      </div>
    </div>
  `
}

function renderStatus(status: StatusResponse) {
  configEl.textContent = `LLM: ${status.config.chatLlm} | Embed: ${status.config.embedding}`

  const { tiers, byType, byScope } = status

  // Tier section
  const tierHtml = `
    <div class="monitor-section">
      <h4 class="section-title">Tiers</h4>
      <div class="tier-item tier-buffer">
        <div class="tier-header">
          <span class="tier-label">T0 Buffer</span>
          <span class="tier-value">${tiers.buffer.count} items</span>
        </div>
        <div class="tier-detail">${tiers.buffer.totalTokens} tokens (est.)</div>
      </div>
      <div class="tier-item tier-short">
        <div class="tier-header">
          <span class="tier-label">T1 Short-term</span>
          <span class="tier-value">${tiers.shortTerm.count}/${tiers.shortTerm.capacity}</span>
        </div>
        <div class="tier-progress">
          <div class="tier-progress-bar" style="width: ${(tiers.shortTerm.count / tiers.shortTerm.capacity) * 100}%"></div>
        </div>
      </div>
      <div class="tier-item tier-long">
        <div class="tier-header">
          <span class="tier-label">T2 Long-term</span>
          <span class="tier-value">${tiers.longTerm.count}</span>
        </div>
        <div class="tier-detail">Session: ${tiers.longTerm.sessionCount} | User: ${tiers.longTerm.userCount}</div>
      </div>
      <div class="tier-item tier-archive ${tiers.archive.count === 0 ? 'tier-inactive' : ''}">
        <div class="tier-header">
          <span class="tier-label">T3 Archive</span>
          <span class="tier-value">${tiers.archive.count}</span>
        </div>
        <div class="tier-detail">${tiers.archive.confirmed} confirmed</div>
      </div>
    </div>
  `

  // Type section
  const typeHtml = `
    <div class="monitor-section">
      <h4 class="section-title">By Type</h4>
      ${Object.entries(byType).map(([k, v]) => `
        <div class="stat-row">
          <span class="stat-label type-${k.toLowerCase()}">${k}</span>
          <span class="stat-value">${v}</span>
        </div>
      `).join('')}
    </div>
  `

  // Scope section
  const scopeHtml = `
    <div class="monitor-section">
      <h4 class="section-title">By Scope</h4>
      <div class="stat-row">
        <span class="stat-label">Session (S1)</span>
        <span class="stat-value">${byScope.session}</span>
      </div>
      <div class="stat-row">
        <span class="stat-label">User (S0)</span>
        <span class="stat-value">${byScope.user}</span>
      </div>
    </div>
  `

  memoryStatsEl.innerHTML = tierHtml + typeHtml + scopeHtml

  // Recent memories
  if (status.recent.length === 0) {
    recentMemoriesEl.innerHTML = '<p style="color:#666;font-size:0.75rem">No memories yet</p>'
  } else {
    recentMemoriesEl.innerHTML = status.recent.map(m => `
      <div class="memory-item ${m.type}">
        <div class="memory-badges">
          <span class="badge badge-tier">${m.tier}</span>
          <span class="badge badge-type">${m.type}</span>
          <span class="badge badge-scope">${m.scope}</span>
        </div>
        <div class="content">${escapeHtml(m.content)}</div>
        <div class="time">${formatTime(m.createdAt)}</div>
      </div>
    `).join('')
  }
}

function addMessage(content: string, type: 'user' | 'assistant' | 'system', meta?: string) {
  const div = document.createElement('div')
  div.className = `message ${type}`
  div.innerHTML = `
    <div class="content">${escapeHtml(content)}</div>
    ${meta ? `<div class="meta">${meta}</div>` : ''}
  `
  messagesEl.appendChild(div)
  messagesEl.scrollTop = messagesEl.scrollHeight
}

// Streaming message element with thinking indicator and reasoning
function createStreamingMessage(): {
  element: HTMLDivElement
  setThinking: (step: string, count?: number) => void
  appendReasoning: (delta: string) => void
  appendContent: (delta: string) => void
  finish: (meta?: string) => void
} {
  const div = document.createElement('div')
  div.className = 'message assistant streaming'

  const thinkingEl = document.createElement('div')
  thinkingEl.className = 'thinking-indicator'
  thinkingEl.innerHTML = '<span class="dot"></span><span class="dot"></span><span class="dot"></span>'

  const reasoningEl = document.createElement('div')
  reasoningEl.className = 'reasoning-section'
  reasoningEl.style.display = 'none'

  const reasoningHeader = document.createElement('div')
  reasoningHeader.className = 'reasoning-header'
  reasoningHeader.innerHTML = '<span class="reasoning-icon">💭</span><span class="reasoning-title">Reasoning</span>'
  reasoningHeader.addEventListener('click', () => {
    reasoningContent.classList.toggle('collapsed')
  })

  const reasoningContent = document.createElement('div')
  reasoningContent.className = 'reasoning-content'

  reasoningEl.appendChild(reasoningHeader)
  reasoningEl.appendChild(reasoningContent)

  const contentEl = document.createElement('div')
  contentEl.className = 'content'
  contentEl.style.display = 'none'

  const metaEl = document.createElement('div')
  metaEl.className = 'meta'
  metaEl.style.display = 'none'

  div.appendChild(thinkingEl)
  div.appendChild(reasoningEl)
  div.appendChild(contentEl)
  div.appendChild(metaEl)
  messagesEl.appendChild(div)
  messagesEl.scrollTop = messagesEl.scrollHeight

  const stepLabels: Record<string, string> = {
    start: 'Thinking...',
    buffer: 'Checking buffer',
    short_term: 'Searching short-term memory',
    long_term: 'Searching long-term memory',
    done: 'Preparing response...'
  }

  return {
    element: div,
    setThinking(step: string, count?: number) {
      console.log('[UI] setThinking:', step, count)
      const label = stepLabels[step] || step
      const countText = count !== undefined ? ` (${count} found)` : ''
      thinkingEl.innerHTML = `<span class="thinking-text">${label}${countText}</span><span class="dot"></span><span class="dot"></span><span class="dot"></span>`
      thinkingEl.style.display = 'flex'
      messagesEl.scrollTop = messagesEl.scrollHeight
    },
    appendReasoning(delta: string) {
      if (reasoningEl.style.display === 'none') {
        thinkingEl.style.display = 'none'
        reasoningEl.style.display = 'block'
      }
      reasoningContent.textContent += delta
      messagesEl.scrollTop = messagesEl.scrollHeight
    },
    appendContent(delta: string) {
      if (contentEl.style.display === 'none') {
        thinkingEl.style.display = 'none'
        // Collapse reasoning when content starts
        if (reasoningEl.style.display === 'block') {
          reasoningContent.classList.add('collapsed')
        }
        contentEl.style.display = 'block'
      }
      contentEl.textContent += delta
      messagesEl.scrollTop = messagesEl.scrollHeight
    },
    finish(meta?: string) {
      div.classList.remove('streaming')
      thinkingEl.remove()
      if (meta) {
        metaEl.textContent = meta
        metaEl.style.display = 'block'
      }
    }
  }
}

// Actions
async function selectUser(user: User) {
  currentUser = user
  currentSession = null

  loginScreen.classList.add('hidden')
  mainScreen.classList.remove('hidden')

  currentUserEl.textContent = user.name

  await renderSessionList()
  await refreshStatus()

  noSessionEl.classList.remove('hidden')
  chatContainerEl.classList.add('hidden')
}

async function selectSession(session: Session) {
  currentSession = session
  messagesEl.innerHTML = ''

  // Reset KPI for new session
  kpiStats.totalTurns = 0
  kpiStats.totalUserTokens = 0
  kpiStats.totalAiTokens = 0
  kpiStats.totalContextTokens = 0
  kpiStats.totalRecallMs = 0
  kpiStats.totalLlmMs = 0
  kpiStats.totalTtftMs = 0
  renderKpi()

  noSessionEl.classList.add('hidden')
  chatContainerEl.classList.remove('hidden')

  addMessage(`Session: ${session.title}`, 'system')

  await renderSessionList()
  messageInput.focus()
}

async function refreshStatus() {
  if (!currentUser) return
  try {
    const status = await api.getStatus(currentUser.id)
    renderStatus(status)
  } catch (err) {
    console.error('Failed to get status:', err)
  }
}

// Event Handlers
createUserBtn.addEventListener('click', async () => {
  const name = newUserNameInput.value.trim()
  if (!name) return

  createUserBtn.setAttribute('disabled', 'true')
  try {
    const user = await api.createUser(name)
    newUserNameInput.value = ''
    selectUser(user)
  } finally {
    createUserBtn.removeAttribute('disabled')
  }
})

newUserNameInput.addEventListener('keypress', (e) => {
  if (e.key === 'Enter') createUserBtn.click()
})

switchUserBtn.addEventListener('click', async () => {
  currentUser = null
  currentSession = null
  mainScreen.classList.add('hidden')
  loginScreen.classList.remove('hidden')
  await renderUserList()
})

newSessionBtn.addEventListener('click', async () => {
  if (!currentUser) return

  newSessionBtn.setAttribute('disabled', 'true')
  try {
    const session = await api.createSession(currentUser.id)
    await selectSession(session)
  } finally {
    newSessionBtn.removeAttribute('disabled')
  }
})

chatForm.addEventListener('submit', async (e) => {
  e.preventDefault()
  if (!currentSession) return

  const message = messageInput.value.trim()
  if (!message) return

  messageInput.value = ''
  addMessage(message, 'user')

  chatForm.classList.add('loading')
  const streamingMsg = createStreamingMessage()

  api.streamMessage(currentSession.id, message, {
    onThinking(data) {
      streamingMsg.setThinking(data.step, data.count)
    },
    onReasoning(delta) {
      streamingMsg.appendReasoning(delta)
    },
    onContent(delta) {
      streamingMsg.appendContent(delta)
    },
    onError(errorMsg) {
      streamingMsg.finish()
      addMessage(`Error: ${errorMsg}`, 'system')
      chatForm.classList.remove('loading')
    },
    onDone(data) {
      // Build meta string with metrics
      let metaParts: string[] = []
      if (data.memoriesUsed > 0) {
        metaParts.push(`📚 ${data.memoriesUsed} memories`)
      }

      if (data.metrics) {
        const m = data.metrics
        metaParts.push(`👤 ${m.userTokens} tok`)
        metaParts.push(`🤖 ${m.aiTokens} tok`)
        if (m.contextTokens > 0) {
          metaParts.push(`📋 ${m.contextTokens} ctx`)
        }
        const llmSpeed = m.llmMs > 0 ? ((m.aiTokens / m.llmMs) * 1000).toFixed(1) : '0'
        metaParts.push(`⚡ ${llmSpeed} tok/s`)
        metaParts.push(`⏱️ ${m.totalMs}ms`)

        // Update KPI
        kpiStats.totalTurns++
        kpiStats.totalUserTokens += m.userTokens
        kpiStats.totalAiTokens += m.aiTokens
        kpiStats.totalContextTokens += m.contextTokens
        kpiStats.totalRecallMs += m.recallMs
        kpiStats.totalLlmMs += m.llmMs
        kpiStats.totalTtftMs += m.ttftMs
        renderKpi()
      }

      streamingMsg.finish(metaParts.length > 0 ? metaParts.join(' | ') : undefined)
      chatForm.classList.remove('loading')

      // Auto-refresh Memory Monitor
      if (currentSession) {
        currentSession.messageCount++
        renderSessionList()
        refreshStatus() // Auto-refresh on response complete
      }
    }
  })
})

refreshStatusBtn.addEventListener('click', async () => {
  refreshStatusBtn.setAttribute('disabled', 'true')
  try {
    await refreshStatus()
  } finally {
    refreshStatusBtn.removeAttribute('disabled')
  }
})

clearMemoriesBtn.addEventListener('click', async () => {
  if (!currentUser) return
  if (!confirm(`Clear all memories for ${currentUser.name}?`)) return

  clearMemoriesBtn.setAttribute('disabled', 'true')
  try {
    await api.clearMemories(currentUser.id)
    await refreshStatus()
    addMessage('All memories cleared', 'system')
  } finally {
    clearMemoriesBtn.removeAttribute('disabled')
  }
})

// Initialize
async function init() {
  try {
    const health = await fetch('/api/health')
    if (!health.ok) throw new Error('Backend not available')

    await renderUserList()
  } catch (err) {
    userListEl.innerHTML = `<p style="color:#e74c3c;text-align:center">Backend not available</p>`
    console.error(err)
  }
}

init()
