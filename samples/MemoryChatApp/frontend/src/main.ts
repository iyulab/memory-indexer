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

interface StatusResponse {
  user: string
  total: number
  byType: Record<string, number>
  bySession: Record<string, number>
  recent: Array<{ content: string; type: string; createdAt: string; importance: number }>
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

function renderStatus(status: StatusResponse) {
  configEl.textContent = `LLM: ${status.config.chatLlm} | Embed: ${status.config.embedding}`

  const typeStats = Object.entries(status.byType)
    .map(([k, v]) => `<div class="stat-row"><span class="stat-label">${k}</span><span class="stat-value">${v}</span></div>`)
    .join('')

  memoryStatsEl.innerHTML = `
    <div class="stat-row">
      <span class="stat-label">Total</span>
      <span class="stat-value">${status.total}</span>
    </div>
    ${typeStats}
  `

  if (status.recent.length === 0) {
    recentMemoriesEl.innerHTML = '<p style="color:#666;font-size:0.75rem">No memories yet</p>'
  } else {
    recentMemoriesEl.innerHTML = status.recent.map(m => `
      <div class="memory-item ${m.type}">
        <span class="type">${m.type}</span>
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
  try {
    const response = await api.sendMessage(currentSession.id, message)
    const meta = response.memoriesUsed > 0 ? `Used ${response.memoriesUsed} memories` : undefined
    addMessage(response.response, 'assistant', meta)

    currentSession.messageCount++
    await renderSessionList()
    await refreshStatus()
  } catch (err) {
    addMessage(`Error: ${err}`, 'system')
  } finally {
    chatForm.classList.remove('loading')
  }
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
