import { ImagePlus, Trash2 } from 'lucide-react'
import { useId, useRef, useState } from 'react'
import { useAction } from '../hooks/useAction'
import { useI18n } from '../i18n/context'
import { FormError } from './forms'
import { Monogram } from './Monogram'

const MAX_BYTES = 2 * 1024 * 1024
const TYPES = ['image/jpeg', 'image/png', 'image/webp']

/** Current image (or initials) with upload / remove buttons: clinic logo, profile photo. */
export function ImageUpload({
  name,
  seed,
  imageId,
  round,
  hint,
  disabled,
  onUpload,
  onRemove,
  onChanged,
}: {
  name: string
  seed?: string
  imageId: string | null | undefined
  round?: boolean
  hint: string
  disabled?: boolean
  onUpload: (file: File) => Promise<unknown>
  onRemove: () => Promise<unknown>
  onChanged: () => void
}) {
  const { t } = useI18n()
  const id = useId()
  const input = useRef<HTMLInputElement>(null)
  const [localError, setLocalError] = useState<string | null>(null)
  const upload = useAction(onUpload)
  const remove = useAction(onRemove)
  const pending = upload.pending || remove.pending

  async function pick(file: File | undefined) {
    if (input.current) input.current.value = ''
    if (!file) return
    if (!TYPES.includes(file.type)) return setLocalError(t('image.badType'))
    if (file.size > MAX_BYTES) return setLocalError(t('image.tooBig'))
    setLocalError(null)
    const r = await upload.run(file)
    if (r.ok) onChanged()
  }

  return (
    <div className="image-upload">
      <Monogram name={name} seed={seed} imageId={imageId} size={88} round={round} />
      <div className="stack stack--sm">
        <div className="row">
          <input ref={input} id={id} type="file" accept={TYPES.join(',')} className="visually-hidden" disabled={disabled || pending} onChange={(e) => pick(e.target.files?.[0])} />
          <button type="button" className="btn btn--ghost btn--small" disabled={disabled || pending} aria-busy={upload.pending || undefined} onClick={() => input.current?.click()}>
            <ImagePlus className="icon" aria-hidden="true" />
            {upload.pending ? t('image.uploading') : imageId ? t('image.replace') : t('image.upload')}
          </button>
          {imageId && (
            <button
              type="button"
              className="btn btn--ghost btn--small"
              disabled={disabled || pending}
              onClick={async () => {
                const r = await remove.run()
                if (r.ok) onChanged()
              }}
            >
              <Trash2 className="icon" aria-hidden="true" />
              {t('image.remove')}
            </button>
          )}
        </div>
        <p className="muted small">{hint}</p>
        {localError && (
          <p className="field__error" role="alert">
            {localError}
          </p>
        )}
        <FormError error={upload.error ?? remove.error} />
      </div>
    </div>
  )
}
