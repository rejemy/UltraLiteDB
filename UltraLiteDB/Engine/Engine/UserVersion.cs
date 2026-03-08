
namespace UltraLiteDB
{
    public partial class UltraLiteEngine
    {
        /// <summary>
        /// Gets or sets a user-defined version number stored in the database header.
        /// Useful for tracking schema migrations.
        /// </summary>
        public ushort UserVersion
        {
            get
            {

                var header = _pager.GetPage<HeaderPage>(0);

                return header.UserVersion;
            
            }
            set
            {
                this.Transaction<bool>(null, false, (col) =>
                {
                    var header = _pager.GetPage<HeaderPage>(0);

                    header.UserVersion = value;

                    _pager.SetDirty(header);

                    return true;
                });
            }
        }
    }
}