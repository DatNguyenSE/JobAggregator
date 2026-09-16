import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, timeout } from 'rxjs';

export interface JobPost {
  id: string;
  facebookGroupId?: string;
  roles?: string[];
  title: string;
  salaryInfo?: string;
  requirements?: string;
  location?: string;
  employerName?: string;
  contactPhone?: string;
  locations?: Array<{ province?: string, district?: string, exactAddress?: string }>;
  workingHours?: string;
  workingDays?: string;
  holidayBonuses?: string;
  otherBenefits?: string;
  sourceUrl: string;
  platform: string;
  externalId: string;
  postedDate: string;

  jobInfo?: string;
  gender?: string;
  vacancies?: number;
  workingFormat?: string;
  ageRequirement?: string;
  experience?: string;
}

export interface SearchCriteriaDto {
  FacebookGroupId?: string;
  Page?: number;
  PageSize?: number;
  // Shared
  Keyword: string;
  Location: string;
  JobType: string;
  Sources: string[];
  MaxJobs: number;
  // Vieclam24h
  Province?: string;
  District?: string;
  // Facebook Groups scraper (apify/facebook-groups-scraper)
  FacebookGroupUrl?: string;
  FacebookViewOption?: 'CHRONOLOGICAL' | 'RECENT_ACTIVITY' | 'TOP_POSTS' | 'CHRONOLOGICAL_LISTINGS';
  FacebookSearchYear?: number;
  FacebookOnlyPostsNewerThan?: string;
}

export interface JobSearchResponse {
  requestId: string | null;
  jobs: JobPost[];
  requestedCount: number;
  missingCount: number;
  status: 'pending' | 'scraping' | 'processing' | 'partial' | 'complete' | 'failed';
  facebookGroupId?: string;
  page?: number;
  hasMore?: boolean;
  isScraping: boolean;
  message?: string;
}

export interface FacebookGroup {
  id: string; name: string; canonicalUrl: string;
  jobCount: number; lastSuccessfulScrapedAt?: string; lastFetchedCount?: number;
}

@Injectable({
  providedIn: 'root'
})
export class JobService {
  // FE local goi API Gateway tren AWS; sua code BE local can deploy Lambda de co hieu luc.
  private apiUrl = 'https://ly17ckpa2h.execute-api.ap-southeast-1.amazonaws.com/Prod/api/jobs';

  constructor(private http: HttpClient) { }

  searchJobs(criteria: SearchCriteriaDto): Observable<JobSearchResponse> {
    // POST chay DB-first: response co the du ngay hoac gom job hien co + requestId dang cao bu.
    return this.http.post<JobSearchResponse>(`${this.apiUrl}/search`, criteria).pipe(timeout(35000));
  }

  getGroups(page = 1): Observable<{ groups: FacebookGroup[], page: number, hasMore: boolean }> {
    return this.http.get<{ groups: FacebookGroup[], page: number, hasMore: boolean }>(`${this.apiUrl}/facebook-groups?page=${page}`).pipe(timeout(15000));
  }

  scrapeJobs(criteria: SearchCriteriaDto, token: string, key: string): Observable<JobSearchResponse> {
    return this.http.post<JobSearchResponse>(`${this.apiUrl}/scrape`, criteria,
      { headers: { 'Idempotency-Key': key } }).pipe(timeout(35000));
  }

  retryProcessing(id: string, token: string): Observable<JobSearchResponse> {
    return this.http.post<JobSearchResponse>(`${this.apiUrl}/scrape/${encodeURIComponent(id)}/retry`, {},
      {}).pipe(timeout(35000));
  }

  getSearchStatus(requestId: string): Observable<JobSearchResponse> {
    // GET chi doc trang thai/ket qua da luu, khong khoi dong them mot lan cao.
    return this.http.get<JobSearchResponse>(`${this.apiUrl}/search/${encodeURIComponent(requestId)}`).pipe(timeout(15000));
  }
}
