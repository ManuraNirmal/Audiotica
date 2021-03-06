﻿#region

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using SQLite;

#endregion

namespace Audiotica.Data.Collection
{
    public interface ISqlService : IDisposable
    {
        SQLiteConnection DbConnection { get; }
        void Initialize(bool walMode = true, bool readOnlyMode = false);
        Task InitializeAsync();
        bool Insert(BaseEntry entry);
        Task<bool> InsertAsync(BaseEntry entry);
        bool DeleteItem(BaseEntry item);
        Task<bool> DeleteItemAsync(BaseEntry item);
        bool UpdateItem(BaseEntry item);
        Task<bool> UpdateItemAsync(BaseEntry item);
        T SelectFirst<T>(Func<T, bool> expression) where T : new();
        Task<T> SelectFirstAsync<T>(Func<T, bool> expression) where T : new();
        List<T> SelectAll<T>() where T : new();
        Task<List<T>> SelectAllAsync<T>() where T : new();
        Task DeleteTableAsync<T>();
        Task DeleteWhereAsync<T>(Expression<Func<T, bool>> express) where T : new();

        void BeginTransaction();
        void Commit();
    }
}