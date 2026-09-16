import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { Subject, of } from 'rxjs';
import { JobListComponent } from './job-list';
import { JobService, JobSearchResponse, FacebookGroup } from '../../services/job';

describe('Stored catalog and explicit scraping', () => {
  let fixture: ComponentFixture<JobListComponent>;
  let component: JobListComponent;
  let api: jasmine.SpyObj<JobService>;
  const group: FacebookGroup = { id: 'g1', name: 'Group one', canonicalUrl: 'https://www.facebook.com/groups/group-one/', jobCount: 4 };
  const response = (pending = false): JobSearchResponse => ({ requestId: pending ? 'r1' : null,
    jobs: [], requestedCount: 20, missingCount: 20, status: pending ? 'processing' : 'complete', isScraping: pending });
  beforeEach(() => {
    localStorage.clear();
    api = jasmine.createSpyObj<JobService>('JobService', ['searchJobs', 'getSearchStatus', 'getGroups', 'scrapeJobs', 'retryProcessing']);
    api.searchJobs.and.returnValue(of(response()));
    api.getGroups.and.returnValue(of({ groups: [group], page: 1, hasMore: false }));
    api.scrapeJobs.and.returnValue(of(response(true)));
    api.getSearchStatus.and.returnValue(of({ ...response(), requestId: 'r1' }));
    TestBed.configureTestingModule({ imports: [JobListComponent], providers: [{ provide: JobService, useValue: api }] });
    fixture = TestBed.createComponent(JobListComponent); component = fixture.componentInstance;
    spyOn(component, 'connectWebSocket'); fixture.detectChanges(); component.vl24Keyword = 'sale';
  });
  afterEach(() => fixture.destroy());
  it('reads DB without scraping even when empty', fakeAsync(() => {
    component.searchJobs(); tick(4000);
    expect(api.searchJobs).toHaveBeenCalledTimes(1); expect(api.scrapeJobs).not.toHaveBeenCalled();
    expect(api.getSearchStatus).not.toHaveBeenCalled();
  }));
  it('loads groups without crawling or automatically selecting a group', () => {
    component.setTab('facebook'); fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.group-card').length).toBe(1);
    expect(api.searchJobs).not.toHaveBeenCalled(); expect(api.scrapeJobs).not.toHaveBeenCalled();
  });
  it('selects a group and filters saved jobs by group identity', () => {
    component.setTab('facebook'); component.selectGroup(group);
    expect(api.searchJobs.calls.mostRecent().args[0].FacebookGroupId).toBe('g1');
    component.fbSearchText = 'bếp'; component.searchJobs();
    expect(api.searchJobs.calls.mostRecent().args[0].Keyword).toBe('bếp');
    expect(api.scrapeJobs).not.toHaveBeenCalled();
  });
  it('paginates through the database', () => {
    component.searchJobs(); component.changePage(1);
    expect(api.searchJobs.calls.mostRecent().args[0].Page).toBe(2);
    expect(api.scrapeJobs).not.toHaveBeenCalled();
  });
  it('explicitly crawls Facebook without keyword or year and polls', fakeAsync(() => {
    component.setTab('facebook'); component.selectGroup(group); component.fbSearchText = 'game';
    component.startScrape();
    const input = api.scrapeJobs.calls.mostRecent().args[0];
    expect(input.Keyword).toBe(''); expect(input.FacebookSearchYear).toBeUndefined();
    expect(input.FacebookGroupId).toBe('g1');
    tick(3000); expect(api.getSearchStatus).toHaveBeenCalledTimes(1);
    expect(api.scrapeJobs).toHaveBeenCalledTimes(1);
  }));
  it('rejects invalid new-group links before starting a crawl', () => {
    component.setTab('facebook'); component.newGroup(); component.facebookGroupUrl = 'https://evil.com/groups/a/';
    component.startScrape(); expect(api.scrapeJobs).not.toHaveBeenCalled(); expect(component.facebookUrlError).toBeTruthy();
  });
  it('does not require login in the functional test UI', () => {
    expect(component.isAdmin).toBeTrue(); expect(fixture.nativeElement.querySelector('#admin-token')).toBeNull();
  });
  it('cancels stale responses on tab changes', () => {
    const old = new Subject<JobSearchResponse>(); api.searchJobs.and.returnValue(old);
    component.searchJobs(); component.setTab('facebook'); old.next({ ...response(), message: 'stale' });
    expect(component.message).not.toBe('stale'); expect(component.jobs).toEqual([]);
  });
  it('renders Facebook duties and schedules without inventing missing benefits', () => {
    component.jobs = [{ id: 'flower', title: 'Nhân viên gói hoa, cắm hoa, soạn và đóng hàng',
      platform: 'facebook', externalId: 'flower', sourceUrl: '', postedDate: '2026-09-15',
      jobInfo: 'Gói hoa, cắm hoa, soạn và đóng hàng', gender: 'Nữ', ageRequirement: '18–25 tuổi',
      workingHours: '8h30–12h; 13h–17h30', workingDays: 'Thứ 2–Thứ 7; Chủ nhật khi tăng ca' }];
    component.expandedJobIds.add('flower'); fixture.detectChanges();
    const card = fixture.nativeElement.querySelector('.job-card');
    expect(card.textContent).toContain('Nhân viên gói hoa, cắm hoa, soạn và đóng hàng');
    expect(card.textContent).toContain('8h30–12h; 13h–17h30');
    expect(card.textContent).toContain('Chủ nhật khi tăng ca');
    expect(card.textContent).toContain('18–25 tuổi');
    expect(card.textContent).not.toContain('Lương thỏa thuận');
    expect(card.textContent).not.toContain('Bất kỳ đâu');
  });
  it('hides empty general information and schedules on sparse posts', () => {
    component.jobs = [{ id: 'sparse', title: 'Tin tuyển dụng', platform: 'facebook',
      externalId: 'sparse', sourceUrl: '', postedDate: '2026-09-15' }];
    component.expandedJobIds.add('sparse'); fixture.detectChanges();
    const card = fixture.nativeElement.querySelector('.job-card');
    expect(card.querySelector('.general-info-container')).toBeNull();
    expect(card.querySelector('.job-meta')).toBeNull();
  });

});
