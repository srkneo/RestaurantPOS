// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { reportError, installErrorReporting, resetErrorReporting } from './errorReport'

describe('browser error reporting', () => {
  let fetchMock

  beforeEach(() => {
    resetErrorReporting()
    fetchMock = vi.fn(() => Promise.resolve({ ok: true }))
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('posts message, path and stack to /api/client-errors', () => {
    reportError('boom', 'at x', 'error')
    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [url, opts] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/client-errors')
    expect(opts.method).toBe('POST')
    const body = JSON.parse(opts.body)
    expect(body.message).toBe('boom')
    expect(body.stack).toBe('at x')
    expect(body.url).toBe(location.pathname)
  })

  it('sends the same error only once', () => {
    reportError('same')
    reportError('same')
    reportError('other')
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('sends at most 20 errors per page', () => {
    for (let i = 0; i < 30; i++) reportError('error ' + i)
    expect(fetchMock).toHaveBeenCalledTimes(20)
  })

  it('cuts long messages and ignores empty ones', () => {
    reportError('')
    reportError('x'.repeat(2000))
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(JSON.parse(fetchMock.mock.calls[0][1].body).message.length).toBe(500)
  })

  it('never throws when the network fails', () => {
    fetchMock.mockImplementation(() => Promise.reject(new Error('offline')))
    expect(() => reportError('no network')).not.toThrow()
  })

  it('catches page errors and unhandled promise errors', () => {
    const target = new EventTarget()
    installErrorReporting(target)
    const err = new Event('error')
    err.message = 'page broke'
    target.dispatchEvent(err)
    const rej = new Event('unhandledrejection')
    rej.reason = new Error('promise broke')
    target.dispatchEvent(rej)
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(JSON.parse(fetchMock.mock.calls[0][1].body).message).toBe('page broke')
    const second = JSON.parse(fetchMock.mock.calls[1][1].body)
    expect(second.message).toBe('promise broke')
    expect(second.source).toBe('promise')
  })
})
