import { Anchor, Baby, CircleEllipsis, Crown, Droplets, HeartPulse, Ruler, ScanSearch, Sparkles, Stethoscope, Syringe, type LucideIcon } from 'lucide-react'
import type { ServiceCategory } from '../../api/types'

export const categoryIcon: Record<ServiceCategory, LucideIcon> = {
  diagnostics: ScanSearch,
  hygiene: Droplets,
  therapy: Stethoscope,
  surgery: Syringe,
  implants: Anchor,
  prosthetics: Crown,
  orthodontics: Ruler,
  pediatric: Baby,
  whitening: Sparkles,
  periodontics: HeartPulse,
  other: CircleEllipsis,
}

export const categoryTone: Record<ServiceCategory, '' | 'teal' | 'violet'> = {
  diagnostics: '',
  hygiene: 'teal',
  therapy: '',
  surgery: 'violet',
  implants: '',
  prosthetics: 'violet',
  orthodontics: 'teal',
  pediatric: 'teal',
  whitening: 'violet',
  periodontics: '',
  other: '',
}
