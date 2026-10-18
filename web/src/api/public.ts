import { query, seg, type ApiClient } from './client'
import type { Availability, ClinicCard, ClinicDetail, ClinicSearchParams, Facets, Page, ReviewDto } from './types'

/** docs/API.md §2 — public catalogue (anonymous). */
export function publicApi(c: ApiClient) {
  return {
    searchClinics: (params: ClinicSearchParams) => c.get<Page<ClinicCard>>(`/public/clinics${query(params)}`),
    clinic: (clinicId: string) => c.get<ClinicDetail>(`/public/clinics/${seg(clinicId)}`),
    reviews: (clinicId: string, page = 1) => c.get<Page<ReviewDto>>(`/public/clinics/${seg(clinicId)}/reviews${query({ page })}`),
    availability: (branchId: string, serviceId: string, date: string) =>
      c.get<Availability>(`/public/branches/${seg(branchId)}/availability${query({ serviceId, date })}`),
    facets: () => c.get<Facets>('/public/facets'),
  }
}
